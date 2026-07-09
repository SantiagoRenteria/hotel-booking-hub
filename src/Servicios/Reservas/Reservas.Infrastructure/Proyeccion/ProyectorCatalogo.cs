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
public sealed class ProyectorCatalogo(ReservasDbContext db) : IConsumidorEventosCatalogo
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public async Task ProcesarAsync(EventoIntegracion evento, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            db.MensajesProcesados.Add(MensajeProcesado.Crear(evento.Id, DateTimeOffset.UtcNow));
            await AplicarAsync(evento, ct);

            await db.SaveChangesAsync(ct); // inbox + proyección: mismo SaveChanges, misma transacción
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is SqlException sql && ClasificacionSqlServer.EsViolacionDeUnico(sql.Number))
        {
            // MessageId ya procesado: el evento es un duplicado → no-op idempotente (todo revierte).
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    private async Task AplicarAsync(EventoIntegracion evento, CancellationToken ct)
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
                    break;
                }

            case PrecioHabitacionCambiadoV1.Tipo:
                {
                    var d = Payload<PrecioHabitacionCambiadoV1>(evento);
                    var fila = await CargarOCrear(d.AggregateId, ct);
                    fila.AplicarPrecio(d.CostoBase, d.Impuestos, evento.Version);
                    break;
                }

            case HabitacionDeshabilitadaV1.Tipo:
                {
                    var d = Payload<HabitacionDeshabilitadaV1>(evento);
                    var fila = await CargarOCrear(d.AggregateId, ct);
                    fila.AplicarDeshabilitada(evento.Version);
                    break;
                }

            // Evento desconocido/no de catálogo: se ignora (el inbox igual lo marca → no se reintenta en bucle).
            default:
                break;
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
