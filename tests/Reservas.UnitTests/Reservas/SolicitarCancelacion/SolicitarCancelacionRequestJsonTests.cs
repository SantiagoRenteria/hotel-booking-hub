using System.Text.Json;
using System.Text.Json.Serialization;
using Reservas.Application.Reservas.SolicitarCancelacion;
using Reservas.Domain.Reservas;
using Xunit;

namespace Reservas.UnitTests.SolicitarCancelacion;

/// <summary>
/// [Review][Patch] — el endpoint registra <see cref="JsonStringEnumConverter"/> para aceptar el iniciador por
/// NOMBRE ("Viajero"/"Agente"), simétrico con el evento (que emite el nombre). Estos tests caracterizan el
/// contrato de binding: sin el converter, el nombre falla; con él, deserializa. El wiring HTTP extremo-a-extremo
/// queda cubierto por el test funcional diferido.
/// </summary>
public sealed class SolicitarCancelacionRequestJsonTests
{
    private const string CuerpoConNombre =
        """{"categoriaMotivo":"CambioDePlanes","detalleMotivo":"detalle","iniciador":"Viajero"}""";

    [Fact]
    public void Con_JsonStringEnumConverter_el_iniciador_bindea_por_nombre()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opciones.Converters.Add(new JsonStringEnumConverter());

        var request = JsonSerializer.Deserialize<SolicitarCancelacionRequest>(CuerpoConNombre, opciones);

        Assert.NotNull(request);
        Assert.Equal(IniciadorCancelacion.Viajero, request!.Iniciador);
    }

    [Fact]
    public void Sin_el_converter_el_nombre_del_enum_no_bindea()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.ThrowsAny<JsonException>(() =>
            JsonSerializer.Deserialize<SolicitarCancelacionRequest>(CuerpoConNombre, opciones));
    }
}
