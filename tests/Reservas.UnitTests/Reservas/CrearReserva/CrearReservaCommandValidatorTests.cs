using FluentValidation.TestHelper;
using Reservas.Application.Reservas.CrearReserva;

namespace Reservas.UnitTests.CrearReserva;

public sealed class CrearReservaCommandValidatorTests
{
    private readonly CrearReservaCommandValidator _validator = new();
    private readonly HuespedDtoValidator _huespedValidator = new();

    [Fact]
    public void Comando_valido_no_tiene_errores()
    {
        var resultado = _validator.TestValidate(DatosPrueba.ComandoValido());
        resultado.ShouldNotHaveAnyValidationErrors();
    }

    // AC-E1.6a.2 — a cada huésped le falta un campo → error de validación en ese campo.
    [Theory]
    [InlineData("Nombres")]
    [InlineData("Apellidos")]
    [InlineData("Genero")]
    [InlineData("TipoDocumento")]
    [InlineData("NumeroDocumento")]
    [InlineData("Email")]
    [InlineData("Telefono")]
    public void Huesped_con_campo_de_texto_vacio_es_invalido(string campo)
    {
        var huesped = VaciarCampo(DatosPrueba.HuespedValido(), campo);

        var resultado = _huespedValidator.TestValidate(huesped);

        resultado.ShouldHaveValidationErrorFor(campo);
    }

    [Fact]
    public void Huesped_sin_fecha_de_nacimiento_es_invalido()
    {
        var huesped = DatosPrueba.HuespedValido() with { FechaNacimiento = default };

        var resultado = _huespedValidator.TestValidate(huesped);

        resultado.ShouldHaveValidationErrorFor(h => h.FechaNacimiento);
    }

    [Fact]
    public void Huesped_con_fecha_de_nacimiento_futura_es_invalido()
    {
        var futuro = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        var huesped = DatosPrueba.HuespedValido() with { FechaNacimiento = futuro };

        var resultado = _huespedValidator.TestValidate(huesped);

        resultado.ShouldHaveValidationErrorFor(h => h.FechaNacimiento);
    }

    [Theory]
    [InlineData("no-es-email")]
    [InlineData("sin-arroba.com")]
    public void Huesped_con_email_invalido_es_rechazado(string email)
    {
        var huesped = DatosPrueba.HuespedValido() with { Email = email };

        var resultado = _huespedValidator.TestValidate(huesped);

        resultado.ShouldHaveValidationErrorFor(h => h.Email);
    }

    [Theory]
    [InlineData("abc")]        // no dígitos
    [InlineData("123")]        // muy corto
    [InlineData("+57 300 12")] // espacios
    public void Huesped_con_telefono_invalido_es_rechazado(string telefono)
    {
        var huesped = DatosPrueba.HuespedValido() with { Telefono = telefono };

        var resultado = _huespedValidator.TestValidate(huesped);

        resultado.ShouldHaveValidationErrorFor(h => h.Telefono);
    }

    [Fact]
    public void Huesped_con_numero_de_documento_invalido_es_rechazado()
    {
        var huesped = DatosPrueba.HuespedValido() with { NumeroDocumento = "??" };

        var resultado = _huespedValidator.TestValidate(huesped);

        resultado.ShouldHaveValidationErrorFor(h => h.NumeroDocumento);
    }

    [Fact]
    public void Comando_sin_huespedes_es_invalido()
    {
        var comando = DatosPrueba.ComandoValido() with { Huespedes = [] };

        var resultado = _validator.TestValidate(comando);

        resultado.ShouldHaveValidationErrorFor(c => c.Huespedes);
    }

    // AC-E1.6a.3 — contacto de emergencia obligatorio.
    [Fact]
    public void Comando_sin_contacto_de_emergencia_es_invalido()
    {
        var comando = DatosPrueba.ComandoValido() with { ContactoEmergencia = null! };

        var resultado = _validator.TestValidate(comando);

        resultado.ShouldHaveValidationErrorFor(c => c.ContactoEmergencia);
    }

    [Fact]
    public void Comando_con_contacto_de_emergencia_sin_telefono_es_invalido()
    {
        var comando = DatosPrueba.ComandoValido() with
        {
            ContactoEmergencia = DatosPrueba.ContactoValido() with { Telefono = "" },
        };

        var resultado = _validator.TestValidate(comando);

        resultado.ShouldHaveValidationErrorFor("ContactoEmergencia.Telefono");
    }

    [Fact]
    public void Comando_con_estancia_que_excede_el_tope_de_noches_es_invalido()
    {
        var comando = DatosPrueba.ComandoValido() with
        {
            Entrada = new DateOnly(2026, 1, 1),
            Salida = new DateOnly(2999, 1, 1), // ~355k noches
        };

        var resultado = _validator.TestValidate(comando);

        resultado.ShouldHaveValidationErrorFor(c => c.Salida);
    }

    [Fact]
    public void Comando_con_salida_no_posterior_a_entrada_es_invalido()
    {
        var comando = DatosPrueba.ComandoValido() with
        {
            Entrada = new DateOnly(2026, 8, 12),
            Salida = new DateOnly(2026, 8, 12),
        };

        var resultado = _validator.TestValidate(comando);

        resultado.ShouldHaveValidationErrorFor(c => c.Salida);
    }

    private static HuespedDto VaciarCampo(HuespedDto h, string campo) => campo switch
    {
        "Nombres" => h with { Nombres = "" },
        "Apellidos" => h with { Apellidos = "" },
        "Genero" => h with { Genero = "" },
        "TipoDocumento" => h with { TipoDocumento = "" },
        "NumeroDocumento" => h with { NumeroDocumento = "" },
        "Email" => h with { Email = "" },
        "Telefono" => h with { Telefono = "" },
        _ => throw new ArgumentOutOfRangeException(nameof(campo), campo, "Campo no contemplado"),
    };
}
