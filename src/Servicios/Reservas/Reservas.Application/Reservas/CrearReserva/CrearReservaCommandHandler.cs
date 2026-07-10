using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;

namespace Reservas.Application.Reservas.CrearReserva;

/// <summary>
/// Orquesta el happy path de crear-confirmar (AC-E1.6a.4): resuelve la habitación, calcula el precio (1.4),
/// crea la <see cref="Reserva"/> con sus slots (1.5) y ENCOLA <c>ReservaConfirmada</c> en el outbox (1.3/1.6b).
/// STAGEA los cambios (sin guardar): el <c>TransactionBehavior</c> confirma dominio + outbox en una sola
/// transacción. El overbooking (2627) sube como <see cref="HabitacionNoDisponibleException"/> desde el
/// <c>SaveChanges</c> y el Api lo mapea a 409; la validación de entrada ya ocurrió en el pipeline.
/// </summary>
public sealed class CrearReservaCommandHandler(
    IDisponibilidadHabitacion disponibilidad,
    IReservaRepository repositorio,
    CalculadorPrecio calculadorPrecio,
    IColaOutbox outbox)
    : IRequestHandler<CrearReservaCommand, Result<ReservaResponseDto>>
{
    private const int VersionEvento = 1;

    public async Task<Result<ReservaResponseDto>> Handle(CrearReservaCommand request, CancellationToken ct)
    {
        var habitacion = await disponibilidad.ObtenerAsync(request.HabitacionId, ct);
        if (habitacion is null || !habitacion.Activa)
        {
            return Result<ReservaResponseDto>.NoEncontrado("La habitación no existe o no está disponible.");
        }

        var estancia = Estancia.Crear(request.Entrada, request.Salida);
        var precioTotal = calculadorPrecio.Calcular(habitacion.CostoBase, habitacion.Impuesto, estancia);

        // VOs de dominio (defensa en profundidad). Story 3.3: se PERSISTEN en el agregado (antes se descartaban).
        var huespedes = request.Huespedes.Select(MapearHuesped).ToList();
        var contacto = ContactoEmergencia.Crear(request.ContactoEmergencia.NombreCompleto, request.ContactoEmergencia.Telefono);

        var reserva = Reserva.Crear(habitacion.Id, estancia, huespedes, contacto, request.AgenteEmail, precioTotal);
        repositorio.Agregar(reserva);

        var principal = huespedes[0];
        var data = new ReservaConfirmadaV1(
            AggregateId: reserva.Id,
            HotelId: habitacion.HotelId,
            HotelNombre: habitacion.HotelNombre,
            Ciudad: habitacion.Ciudad,
            HabitacionId: habitacion.Id,
            Entrada: reserva.Estancia.Entrada,
            Salida: reserva.Estancia.Salida,
            HuespedNombre: principal.NombreCompleto,
            HuespedEmail: principal.Email,
            AgenteEmail: request.AgenteEmail,
            PrecioTotal: precioTotal);

        outbox.Encolar(ReservaConfirmadaV1.Tipo, VersionEvento, reserva.Id, data, Activity.Current?.TraceId.ToString());

        return Result<ReservaResponseDto>.Ok(new ReservaResponseDto(reserva.Id, reserva.Estado.ToString(), precioTotal));
    }

    private static Huesped MapearHuesped(HuespedDto d) =>
        Huesped.Crear(
            d.Nombres,
            d.Apellidos,
            d.FechaNacimiento,
            d.Genero,
            Documento.Crear(d.TipoDocumento, d.NumeroDocumento),
            d.Email,
            d.Telefono);
}
