using System.Text.Json;
using System.Text.Json.Nodes;
using HotelBookingHub.Comun.Eventos;
using Xunit;

namespace Contracts;

/// <summary>
/// Contract test (Story 2.5, AC-E2.5.1): congela la FORMA de los eventos de catálogo que produce el BC de
/// Hoteles y consumirá la proyección de disponibilidad de E3. Un cambio incompatible —envelope, semver del
/// <c>type</c>, order key (<c>data.aggregateId</c> + <c>version</c>), o las claves de data— rompe estas
/// aserciones. Rápido, sin contenedores; corre en cada PR. La SEMÁNTICA (orden/idempotencia en E3) no se prueba aquí.
/// </summary>
public sealed class ContratoEventosCatalogoTests
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    private static readonly Guid _hab = Guid.Parse("019f43ef-0000-7000-8000-000000000a01");
    private static readonly Guid _hotel = Guid.Parse("019f43ef-0000-7000-8000-000000000b01");

    public static IEnumerable<object[]> Eventos()
    {
        yield return [
            HabitacionAgregadaV1.Tipo,
            new HabitacionAgregadaV1(_hab, _hotel, "Suite", 100.00m, 19.00m, "Piso 3", "Habilitada"),
            new[] { "aggregateId", "hotelId", "tipoHabitacion", "costoBase", "impuestos", "ubicacion", "estado" }];
        yield return [
            PrecioHabitacionCambiadoV1.Tipo,
            new PrecioHabitacionCambiadoV1(_hab, _hotel, 150.00m, 28.50m),
            new[] { "aggregateId", "hotelId", "costoBase", "impuestos" }];
        yield return [
            HabitacionDeshabilitadaV1.Tipo,
            new HabitacionDeshabilitadaV1(_hab, _hotel),
            new[] { "aggregateId", "hotelId" }];
    }

    private static EventoIntegracion Envelope(string tipo, object data) => new(
        Id: Guid.Parse("019f43ef-0000-7000-8000-000000000001"),
        Type: tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        Data: data);

    [Theory]
    [MemberData(nameof(Eventos))]
    public void Envelope_conserva_claves_y_type_lleva_semver(string tipo, object data, string[] _)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(Envelope(tipo, data), _opciones))!.AsObject();

        Assert.Equal(
            new[] { "id", "type", "version", "occurredAt", "traceId", "data" }.OrderBy(k => k),
            root.Select(p => p.Key).OrderBy(k => k));

        Assert.True(Guid.TryParse((string?)root["id"], out var id) && id != Guid.Empty); // dedup key
        Assert.Equal(tipo, (string?)root["type"]);
        Assert.Matches(@"^[A-Za-z]+\.v\d+$", (string?)root["type"]!); // PascalCase + semver
        Assert.True(root["version"]!.GetValue<int>() >= 1); // order key (parte 2)
    }

    [Theory]
    [MemberData(nameof(Eventos))]
    public void Data_conserva_las_claves_congeladas_y_el_order_key(string tipo, object data, string[] clavesEsperadas)
    {
        var dataNode = JsonNode.Parse(JsonSerializer.Serialize(Envelope(tipo, data), _opciones))!["data"]!.AsObject();

        Assert.Equal(clavesEsperadas.OrderBy(k => k), dataNode.Select(p => p.Key).OrderBy(k => k));

        // Order key (parte 1): aggregateId presente y no vacío en todos los eventos de catálogo.
        Assert.True(Guid.TryParse((string?)dataNode["aggregateId"], out var agg) && agg != Guid.Empty);
    }

    [Fact]
    public void Los_tipos_llevan_el_nombre_y_semver_esperados()
    {
        Assert.Equal("HabitacionAgregada.v1", HabitacionAgregadaV1.Tipo);
        Assert.Equal("PrecioHabitacionCambiado.v1", PrecioHabitacionCambiadoV1.Tipo);
        Assert.Equal("HabitacionDeshabilitada.v1", HabitacionDeshabilitadaV1.Tipo);
    }
}
