using Reservas.Application.Reservas.CrearReserva;

namespace Reservas.UnitTests.CrearReserva;

/// <summary>Constructores de comandos/huéspedes válidos que cada test muta para provocar el fallo bajo prueba.</summary>
internal static class DatosPrueba
{
    public static HuespedDto HuespedValido() => new(
        Nombres: "Juan",
        Apellidos: "García",
        FechaNacimiento: new DateOnly(1990, 5, 20),
        Genero: "Masculino",
        TipoDocumento: "CC",
        NumeroDocumento: "1234567",
        Email: "juan@correo.com",
        Telefono: "+573001234567");

    public static ContactoEmergenciaDto ContactoValido() => new("María Pérez", "+573007654321");

    public static CrearReservaCommand ComandoValido() => new(
        HabitacionId: Guid.NewGuid(),
        Entrada: new DateOnly(2026, 8, 10),
        Salida: new DateOnly(2026, 8, 12),
        Huespedes: [HuespedValido()],
        ContactoEmergencia: ContactoValido(),
        AgenteEmail: "agente@hotel.com");
}
