using System.Text.Json;
using System.Text.Json.Nodes;
using HotelBookingHub.Comun.Eventos;
using Xunit;

namespace Contracts;

/// <summary>
/// Contract test (Story 4.2, Task 4): congela la FORMA de los eventos de resolución de cancelación que produce el
/// BC de Reservas y consumirá E5 — <c>ReservaCancelada.v1</c> (aprobar) y <c>SolicitudCancelacionRechazada.v1</c>
/// (rechazar). Test propio de eventos de reserva (separado del de catálogo). Un cambio incompatible en el envelope,
/// el semver del <c>type</c>, la order key (<c>data.aggregateId</c> + <c>version</c>) o las claves de data rompe
/// estas aserciones.
/// </summary>
public sealed class ContratoEventosResolucionCancelacionTests
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    public static IEnumerable<object[]> Eventos()
    {
        yield return [
            ReservaCanceladaV1.Tipo,
            new ReservaCanceladaV1(
                AggregateId: Guid.Parse("019f43ef-0000-7000-8000-0000000002aa"),
                ResueltaPor: "agente@hotel.com",
                FechaResolucion: new DateOnly(2026, 8, 20),
                PenalidadAplicadaPorcentaje: 100m,
                PenalidadFueOverride: false,
                HuespedEmail: "andres@example.com"),
            new[] { "aggregateId", "resueltaPor", "fechaResolucion", "penalidadAplicadaPorcentaje", "penalidadFueOverride", "huespedEmail" }];
        yield return [
            SolicitudCancelacionRechazadaV1.Tipo,
            new SolicitudCancelacionRechazadaV1(
                AggregateId: Guid.Parse("019f43ef-0000-7000-8000-0000000002bb"),
                ResueltaPor: "agente@hotel.com",
                FechaResolucion: new DateOnly(2026, 8, 20),
                MotivoRechazo: "No procede la cancelación.",
                HuespedEmail: "andres@example.com"),
            new[] { "aggregateId", "resueltaPor", "fechaResolucion", "motivoRechazo", "huespedEmail" }];
    }

    private static EventoIntegracion Envelope(string tipo, object data) => new(
        Id: Guid.Parse("019f43ef-0000-7000-8000-000000000201"),
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
        Assert.True(Guid.TryParse((string?)root["id"], out var id) && id != Guid.Empty);
        Assert.Equal(tipo, (string?)root["type"]);
        Assert.Matches(@"^[A-Za-z]+\.v\d+$", (string?)root["type"]!);
        Assert.True(root["version"]!.GetValue<int>() >= 1);
    }

    [Theory]
    [MemberData(nameof(Eventos))]
    public void Data_conserva_las_claves_congeladas_y_el_order_key(string tipo, object data, string[] clavesEsperadas)
    {
        var dataNode = JsonNode.Parse(JsonSerializer.Serialize(Envelope(tipo, data), _opciones))!["data"]!.AsObject();

        Assert.Equal(clavesEsperadas.OrderBy(k => k), dataNode.Select(p => p.Key).OrderBy(k => k));
        Assert.True(Guid.TryParse((string?)dataNode["aggregateId"], out var agg) && agg != Guid.Empty); // order key
        Assert.Equal("2026-08-20", (string?)dataNode["fechaResolucion"]); // DateOnly yyyy-MM-dd
    }

    [Fact]
    public void Los_tipos_llevan_el_nombre_y_semver_esperados()
    {
        Assert.Equal("ReservaCancelada.v1", ReservaCanceladaV1.Tipo);
        Assert.Equal("SolicitudCancelacionRechazada.v1", SolicitudCancelacionRechazadaV1.Tipo);
    }
}
