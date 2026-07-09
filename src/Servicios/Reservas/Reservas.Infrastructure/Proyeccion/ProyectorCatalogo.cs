using System.Data;
using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Reservas.Application.Abstracciones;
using Reservas.Infrastructure.Idempotencia;
using Reservas.Infrastructure.Persistencia;

namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Consumidor EF Core de los eventos de catálogo (Story 3.1). En UNA sola transacción READ COMMITTED:
/// registra el <c>MessageId</c> en el inbox (dedup) y aplica el evento a la <see cref="ProyeccionHabitacion"/>
/// (CRDT por field-ownership). Si el <c>MessageId</c> ya existe (PK duplicada), el <c>SaveChanges</c> falla y se
/// revierte TODO: el evento no se re-aplica (idempotencia dura, AC-E3.1.2/4). La convergencia bajo desorden la
/// da el propio read-model (guardas por versión de bloque), no este consumidor.
/// </summary>
public sealed class ProyectorCatalogo(ReservasDbContext db, IInvalidadorCacheDisponibilidad? invalidadorCache = null)
    : IConsumidorEventosCatalogo
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public async Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        // El inbox se inserta en su PROPIO SaveChanges (dentro de la misma transacción): así una violación de
        // único aquí es INEQUÍVOCAMENTE el dedup del MessageId (no se puede confundir con la PK de la proyección).
        // Un evento ya procesado → no-op idempotente (rollback de la tx vacía). Cualquier OTRA violación (p. ej.
        // la PK de ProyeccionHabitacion en una carrera de creación concurrente) NO se traga: se propaga para que
        // el transporte at-least-once reentregue y converja — nunca se pierde en silencio.
        db.MensajesProcesados.Add(MensajeProcesado.Crear(evento.Id, DateTimeOffset.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException sql && ClasificacionSqlServer.EsViolacionDeUnico(sql.Number))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return;
        }

        var ciudadAfectada = await AplicarAsync(evento, ct);
        await db.SaveChangesAsync(ct); // proyección; commitea junto con el inbox
        await tx.CommitAsync(ct);

        // Invalidación dirigida de la caché de lectura (AC-E3.2.3), FUERA de la transacción del read-model: si
        // Redis fallara no debe revertir la proyección YA confirmada. Se envuelve en try/catch (best-effort): un
        // fallo de Redis no debe propagar tras el commit (haría que la reentrega salte por el dedup del inbox y la
        // invalidación se pierda para siempre); la caché se autocorrige por TTL. Solo si hay ciudad conocida: una
        // fila de habitación parcial (no hidratada) no aparece en la búsqueda → no hay nada que invalidar.
        if (invalidadorCache is not null && ciudadAfectada is { } ciudad)
        {
            try
            {
                await invalidadorCache.InvalidarCiudadAsync(ciudad, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: la proyección ya está confirmada; la caché converge por TTL.
            }
        }
    }

    /// <summary>Aplica el evento y devuelve la CIUDAD afectada (para invalidar su caché), o null si no aplica.</summary>
    private async Task<string?> AplicarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        switch (evento.Type)
        {
            case HabitacionAgregadaV1.Tipo:
                {
                    var d = Payload<HabitacionAgregadaV1>(evento);
                    var fila = await CargarOCrear(d.AggregateId, ct);
                    fila.AplicarAgregada(
                        d.HotelId, d.Ciudad, d.TipoHabitacion, d.Ubicacion, d.Capacidad,
                        d.CostoBase, d.Impuestos, d.Estado, evento.Version);
                    return fila.Ciudad;
                }

            case PrecioHabitacionCambiadoV1.Tipo:
                {
                    var d = Payload<PrecioHabitacionCambiadoV1>(evento);
                    var fila = await CargarOCrear(d.AggregateId, ct);
                    fila.AplicarPrecio(d.CostoBase, d.Impuestos, evento.Version);
                    return fila.Ciudad;
                }

            case HabitacionDeshabilitadaV1.Tipo:
                {
                    var d = Payload<HabitacionDeshabilitadaV1>(evento);
                    var fila = await CargarOCrear(d.AggregateId, ct);
                    fila.AplicarDeshabilitada(evento.Version);
                    return fila.Ciudad;
                }

            case HotelDeshabilitadoV1.Tipo:
                {
                    var d = Payload<HotelDeshabilitadoV1>(evento);
                    var fila = await CargarOCrearHotel(d.AggregateId, ct);
                    fila.AplicarDeshabilitado(evento.Version);
                    return d.Ciudad;
                }

            case HotelHabilitadoV1.Tipo:
                {
                    var d = Payload<HotelHabilitadoV1>(evento);
                    var fila = await CargarOCrearHotel(d.AggregateId, ct);
                    fila.AplicarHabilitado(evento.Version);
                    return d.Ciudad;
                }

            // Evento desconocido/no de catálogo: se ignora (el inbox igual lo marca → no se reintenta en bucle).
            default:
                return null;
        }
    }

    private async Task<ProyeccionHabitacion> CargarOCrear(Guid habitacionId, CancellationToken ct)
    {
        var fila = await db.ProyeccionesHabitacion.FirstOrDefaultAsync(p => p.HabitacionId == habitacionId, ct);
        if (fila is null)
        {
            fila = ProyeccionHabitacion.Nueva(habitacionId); // delta antes del alta → fila parcial
            db.ProyeccionesHabitacion.Add(fila);
        }

        return fila;
    }

    private async Task<ProyeccionHotelEstado> CargarOCrearHotel(Guid hotelId, CancellationToken ct)
    {
        var fila = await db.ProyeccionesHotelEstado.FirstOrDefaultAsync(p => p.HotelId == hotelId, ct);
        if (fila is null)
        {
            // El evento de hotel puede llegar ANTES del alta de sus habitaciones (fila huérfana, correcta por join).
            fila = ProyeccionHotelEstado.Nueva(hotelId);
            db.ProyeccionesHotelEstado.Add(fila);
        }

        return fila;
    }

    private static T Payload<T>(EventoIntegracion evento)
    {
        // Tras el transporte, Data llega como JsonElement (no como el tipo concreto): se deserializa aquí.
        if (evento.Data is T tipado)
        {
            return tipado;
        }

        var json = JsonSerializer.Serialize(evento.Data, _opciones);
        return JsonSerializer.Deserialize<T>(json, _opciones)!;
    }
}
