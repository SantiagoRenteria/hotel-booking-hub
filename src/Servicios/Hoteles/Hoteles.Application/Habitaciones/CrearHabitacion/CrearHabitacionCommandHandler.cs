using System.Diagnostics;
using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Habitaciones;
using Hoteles.Domain.Puertos;

namespace Hoteles.Application.Habitaciones.CrearHabitacion;

/// <summary>
/// Añade una habitación (AC-E2.4.1). Verifica que el hotel EXISTA (no eliminado; el query filter de soft-delete
/// hace que un hotel eliminado no se encuentre → 404). Un hotel <b>deshabilitado</b> SÍ admite habitaciones (se
/// prepara el catálogo antes de publicar; AC-E2.4.3 asume habitaciones bajo hoteles deshabilitados). Persiste la
/// habitación y devuelve 201 con su <c>rowVersion</c> inicial. NO altera el hotel (independencia de aggregates).
/// <para>
/// Trade-off asumido: es un check-then-act sin FK (independencia de aggregates). Si el hotel se elimina justo
/// entre la verificación y el INSERT, la habitación quedaría «huérfana» respecto a un hotel invisible; la
/// proyección/consumo de E3 la filtra aguas abajo. No se fuerza FK para no acoplar los aggregates.
/// </para>
/// </summary>
public sealed class CrearHabitacionCommandHandler(
    IHotelRepository hoteles, IHabitacionRepository habitaciones, IColaOutbox outbox)
    : IRequestHandler<CrearHabitacionCommand, Result<HabitacionResponseDto>>
{
    public async Task<Result<HabitacionResponseDto>> Handle(CrearHabitacionCommand request, CancellationToken ct)
    {
        var hotel = await hoteles.ObtenerAsync(request.HotelId, ct);
        if (hotel is null)
        {
            return Result<HabitacionResponseDto>.NoEncontrado($"No existe un hotel con id {request.HotelId}.");
        }

        var habitacion = Habitacion.Crear(
            request.HotelId, request.Tipo, request.CostoBase, request.Impuestos, request.Ubicacion, request.Estado,
            request.Capacidad);

        // Guarda de borde de emisión (party-mode): la ciudad denormalizada NO puede ir vacía o envenenaría el
        // read-model de E3 (búsqueda por ciudad → resultados fantasma). El invariante del Hotel ya la garantiza.
        if (string.IsNullOrWhiteSpace(hotel.Ciudad))
        {
            throw new InvalidOperationException($"El hotel {hotel.Id} no tiene ciudad; no se puede emitir HabitacionAgregada.");
        }

        // Encola el evento de catálogo ANTES del SaveChanges: la fila de outbox y la habitación se persisten en
        // la MISMA transacción implícita del único SaveChanges del repositorio (atomicidad AC-E2.5.2, Opción U).
        // Ciudad (del hotel) y Capacidad se denormalizan para la proyección de E3 (party-mode A2).
        var data = new HabitacionAgregadaV1(
            habitacion.Id, habitacion.HotelId, habitacion.Tipo, habitacion.CostoBase, habitacion.Impuestos,
            habitacion.Ubicacion, habitacion.Estado.ToString(), hotel.Ciudad, habitacion.Capacidad);
        outbox.Encolar(HabitacionAgregadaV1.Tipo, habitacion.Version, habitacion.Id, data,
            Activity.Current?.TraceId.ToString());

        var rowVersion = await habitaciones.CrearAsync(habitacion, ct);

        return Result<HabitacionResponseDto>.Ok(HabitacionResponseDto.De(habitacion, rowVersion));
    }
}
