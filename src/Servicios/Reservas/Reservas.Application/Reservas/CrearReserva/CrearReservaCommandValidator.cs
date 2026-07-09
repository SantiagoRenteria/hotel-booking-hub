using FluentValidation;

namespace Reservas.Application.Reservas.CrearReserva;

/// <summary>
/// Reglas de validación del comando (FR-10/11): todos los campos de cada huésped y el contacto de
/// emergencia son obligatorios y con formato válido. El <c>ValidationBehavior</c> corta con 400 antes del handler.
/// </summary>
public sealed class CrearReservaCommandValidator : AbstractValidator<CrearReservaCommand>
{
    public CrearReservaCommandValidator()
    {
        RuleFor(x => x.HabitacionId)
            .NotEmpty().WithMessage("La habitación es obligatoria.");

        RuleFor(x => x.AgenteEmail)
            .NotEmpty().WithMessage("El correo del agente es obligatorio.")
            .EmailAddress().WithMessage("El correo del agente no tiene un formato válido.");

        RuleFor(x => x.Salida)
            .GreaterThan(x => x.Entrada).WithMessage("La fecha de salida debe ser posterior a la de entrada.");

        RuleFor(x => x.Huespedes)
            .NotEmpty().WithMessage("Debe registrar al menos un huésped.");
        RuleForEach(x => x.Huespedes).SetValidator(new HuespedDtoValidator());

        RuleFor(x => x.ContactoEmergencia)
            .NotNull().WithMessage("El contacto de emergencia es obligatorio.")
            .SetValidator(new ContactoEmergenciaDtoValidator());
    }
}

/// <summary>Cada campo del huésped obligatorio + formato (email/teléfono/documento; fecha coherente).</summary>
public sealed class HuespedDtoValidator : AbstractValidator<HuespedDto>
{
    public HuespedDtoValidator()
    {
        RuleFor(h => h.Nombres).NotEmpty().WithMessage("Los nombres son obligatorios.");
        RuleFor(h => h.Apellidos).NotEmpty().WithMessage("Los apellidos son obligatorios.");

        RuleFor(h => h.FechaNacimiento)
            .NotEqual(default(DateOnly)).WithMessage("La fecha de nacimiento es obligatoria.")
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("La fecha de nacimiento debe ser una fecha pasada.");

        RuleFor(h => h.Genero).NotEmpty().WithMessage("El género es obligatorio.");
        RuleFor(h => h.TipoDocumento).NotEmpty().WithMessage("El tipo de documento es obligatorio.");

        RuleFor(h => h.NumeroDocumento)
            .NotEmpty().WithMessage("El número de documento es obligatorio.")
            .Must(FormatoDocumentoValido).WithMessage("El número de documento no tiene un formato válido.");

        RuleFor(h => h.Email)
            .NotEmpty().WithMessage("El email es obligatorio.")
            .EmailAddress().WithMessage("El email no tiene un formato válido.");

        RuleFor(h => h.Telefono)
            .NotEmpty().WithMessage("El teléfono es obligatorio.")
            .Must(FormatoTelefonoValido).WithMessage("El teléfono no tiene un formato válido.");
    }

    // Si viene vacío lo señala NotEmpty; aquí solo validamos el formato de un valor presente.
    private static bool FormatoDocumentoValido(string? numero) =>
        string.IsNullOrEmpty(numero) || ExpresionesValidacion.NumeroDocumento().IsMatch(numero);

    private static bool FormatoTelefonoValido(string? telefono) =>
        string.IsNullOrEmpty(telefono) || ExpresionesValidacion.Telefono().IsMatch(telefono);
}

/// <summary>Contacto de emergencia: nombre completo + teléfono válido, obligatorio.</summary>
public sealed class ContactoEmergenciaDtoValidator : AbstractValidator<ContactoEmergenciaDto>
{
    public ContactoEmergenciaDtoValidator()
    {
        RuleFor(c => c.NombreCompleto).NotEmpty().WithMessage("El nombre del contacto de emergencia es obligatorio.");
        RuleFor(c => c.Telefono)
            .NotEmpty().WithMessage("El teléfono del contacto de emergencia es obligatorio.")
            .Must(t => string.IsNullOrEmpty(t) || ExpresionesValidacion.Telefono().IsMatch(t))
            .WithMessage("El teléfono del contacto de emergencia no tiene un formato válido.");
    }
}
