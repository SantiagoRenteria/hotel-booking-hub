using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Reservas.Domain.Puertos;
using Reservas.Domain.Reservas;
using Reservas.Domain.Servicios;

namespace Reservas.Application.Reservas.CrearReserva;

/// <summary>
/// Orquesta el happy path de crear-confirmar (AC-E1.6a.4): resuelve la habitación, calcula el precio (1.4),
/// crea la <see cref="Reserva"/> e inserta sus slots (1.5) y publica <c>ReservaConfirmada</c> (1.3) vía el
/// puerto de eventos (fake en tests, Dapr+outbox en 1.6b). La validación de entrada ya ocurrió en el pipeline.
/// </summary>
public sealed class CrearReservaCommandHandler(
    IDisponibilidadHabitacion disponibilidad,
    IReservaRepository repositorio,
    CalculadorPrecio calculadorPrecio,
    IPublicadorEventos publicador)
    : IRequestHandler<CrearReservaCommand, Result<ReservaResponseDto>>
{
    private const string TopicoReservas = "reservas";

    public async Task<Result<ReservaResponseDto>> Handle(CrearReservaCommand request, CancellationToken ct)
    {
        var habitacion = await disponibilidad.ObtenerAsync(request.HabitacionId, ct);
        if (habitacion is null || !habitacion.Activa)
        {
            return Result<ReservaResponseDto>.NoEncontrado("La habitación no existe o no está disponible.");
        }

        var estancia = Estancia.Crear(request.Entrada, request.Salida);
        var precioTotal = calculadorPrecio.Calcular(habitacion.CostoBase, habitacion.Impuesto, estancia);

        // VOs de dominio (defensa en profundidad). Persistir huéspedes/contacto en el agregado (mapeo EF +
        // migración) se difiere a 1.6b, cuando la escritura se prueba de forma transaccional/atómica.
        var huespedes = request.Huespedes.Select(MapearHuesped).ToList();
        _ = ContactoEmergencia.Crear(request.ContactoEmergencia.NombreCompleto, request.ContactoEmergencia.Telefono);

        var reserva = Reserva.Crear(habitacion.Id, estancia);

        try
        {
            await repositorio.ConfirmarAsync(reserva, ct);
        }
        catch (HabitacionNoDisponibleException)
        {
            // Otro ganó la carrera por esa noche (arbitraje del motor, 1.5). El endurecimiento bajo
            // concurrencia (money test G1) es 1.6c; aquí basta traducir a conflicto (409).
            return Result<ReservaResponseDto>.Conflicto("La habitación no está disponible para las fechas seleccionadas.");
        }

        var principal = huespedes[0];
        await publicador.PublicarAsync(TopicoReservas, ConstruirEvento(reserva, habitacion, principal, request.AgenteEmail, precioTotal), ct);

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

    private static EventoIntegracion ConstruirEvento(
        Reserva reserva,
        HabitacionReservable habitacion,
        Huesped principal,
        string agenteEmail,
        decimal precioTotal) =>
        new(
            Id: Guid.CreateVersion7(),
            Type: ReservaConfirmadaV1.Tipo,
            Version: 1,
            OccurredAt: DateTimeOffset.UtcNow,
            TraceId: Activity.Current?.TraceId.ToString(),
            Data: new ReservaConfirmadaV1(
                AggregateId: reserva.Id,
                HotelId: habitacion.HotelId,
                HotelNombre: habitacion.HotelNombre,
                Ciudad: habitacion.Ciudad,
                HabitacionId: habitacion.Id,
                Entrada: reserva.Estancia.Entrada,
                Salida: reserva.Estancia.Salida,
                HuespedNombre: principal.NombreCompleto,
                HuespedEmail: principal.Email,
                AgenteEmail: agenteEmail,
                PrecioTotal: precioTotal));
}
