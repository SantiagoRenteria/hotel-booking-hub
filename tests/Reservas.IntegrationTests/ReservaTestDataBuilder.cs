using Reservas.Application.Reservas.CrearReserva;

namespace Reservas.IntegrationTests;

/// <summary>
/// ObjectMother de un <see cref="CrearReservaCommand"/> válido y reproducible (AC-E1.6c.1): mismos datos
/// de huésped/contacto en cada corrida; solo varían habitación y estancia. El "seed" del escenario del money
/// test es una habitación + 1 noche libre (la disponibilidad sembrada trata cualquier habitación como activa).
/// </summary>
internal sealed class ReservaTestDataBuilder
{
    private Guid _habitacionId = Guid.NewGuid();
    private DateOnly _entrada = new(2026, 9, 1);
    private DateOnly _salida = new(2026, 9, 2); // 1 noche

    public ReservaTestDataBuilder ParaHabitacion(Guid habitacionId)
    {
        _habitacionId = habitacionId;
        return this;
    }

    public ReservaTestDataBuilder ConEstancia(DateOnly entrada, DateOnly salida)
    {
        _entrada = entrada;
        _salida = salida;
        return this;
    }

    public CrearReservaCommand Construir() => new(
        HabitacionId: _habitacionId,
        Entrada: _entrada,
        Salida: _salida,
        Huespedes:
        [
            new HuespedDto(
                Nombres: "Juan",
                Apellidos: "García",
                FechaNacimiento: new DateOnly(1990, 5, 20),
                Genero: "Masculino",
                TipoDocumento: "CC",
                NumeroDocumento: "1234567",
                Email: "juan@correo.com",
                Telefono: "+573001234567"),
        ],
        ContactoEmergencia: new ContactoEmergenciaDto("María Pérez", "+573007654321"),
        AgenteEmail: "agente@hotel.com");
}
