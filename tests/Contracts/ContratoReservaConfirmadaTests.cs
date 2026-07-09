using System.Text.Json;
using System.Text.Json.Nodes;
using HotelBookingHub.Comun.Eventos;
using Xunit;

namespace Contracts;

/// <summary>
/// Contract test (Story 1.3, AC-E1.3.1): congela la FORMA del evento de integración
/// <c>ReservaConfirmada.v1</c> (envelope + data). Un cambio incompatible en las claves —dedup
/// (<c>id</c>), orden (<c>aggregateId</c> + <c>version</c>), <c>type</c> con semver, o los campos
/// que consume Notificaciones— rompe estas aserciones. Rápido, sin contenedores; corre en cada PR.
/// La SEMÁNTICA (dedupe en E5, orden en E3) NO se prueba aquí, solo la forma.
/// </summary>
public sealed class ContratoReservaConfirmadaTests
{
    // Web defaults = camelCase + case-insensitive (mismas opciones que usa el transporte).
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    private static EventoIntegracion EventoDeEjemplo() => new(
        Id: Guid.Parse("019f43ef-0000-7000-8000-000000000001"),
        Type: ReservaConfirmadaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        Data: new ReservaConfirmadaV1(
            AggregateId: Guid.Parse("019f43ef-0000-7000-8000-0000000000aa"),
            HotelId: Guid.Parse("019f43ef-0000-7000-8000-0000000000bb"),
            HotelNombre: "Hotel Cartagena Real",
            Ciudad: "Cartagena",
            HabitacionId: Guid.Parse("019f43ef-0000-7000-8000-0000000000cc"),
            Entrada: new DateOnly(2026, 8, 10),
            Salida: new DateOnly(2026, 8, 14),
            HuespedNombre: "Andrés Pérez",
            HuespedEmail: "andres@example.com",
            AgenteEmail: "carolina@agencia.com",
            PrecioTotal: 1428.00m));

    [Fact]
    public void Envelope_conserva_sus_claves_y_el_type_lleva_semver()
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(EventoDeEjemplo(), _opciones))!.AsObject();

        Assert.Equal(
            new[] { "id", "type", "version", "occurredAt", "traceId", "data" }.OrderBy(k => k),
            root.Select(p => p.Key).OrderBy(k => k));

        // Dedup key: id presente y no vacío.
        Assert.True(Guid.TryParse((string?)root["id"], out var id) && id != Guid.Empty);
        // type = PascalCase español + semver (.vN).
        Assert.Equal("ReservaConfirmada.v1", (string?)root["type"]);
        Assert.Matches(@"^[A-Za-z]+\.v\d+$", (string?)root["type"]!);
        // Order key (parte 2): version presente.
        Assert.True(root["version"]!.GetValue<int>() >= 1);
        Assert.NotNull(root["occurredAt"]);
    }

    [Fact]
    public void Data_conserva_las_claves_congeladas_y_los_formatos()
    {
        var data = JsonNode.Parse(JsonSerializer.Serialize(EventoDeEjemplo(), _opciones))!["data"]!.AsObject();

        // Un rename/quita de cualquiera de estas claves rompe el contrato (y este test).
        Assert.Equal(
            new[]
            {
                "aggregateId", "hotelId", "hotelNombre", "ciudad", "habitacionId",
                "entrada", "salida", "huespedNombre", "huespedEmail", "agenteEmail", "precioTotal",
            }.OrderBy(k => k),
            data.Select(p => p.Key).OrderBy(k => k));

        // Order key (parte 1): aggregateId presente y no vacío.
        Assert.True(Guid.TryParse((string?)data["aggregateId"], out var agg) && agg != Guid.Empty);
        // DateOnly como yyyy-MM-dd.
        Assert.Equal("2026-08-10", (string?)data["entrada"]);
        Assert.Equal("2026-08-14", (string?)data["salida"]);
        // Dinero como número decimal, nunca string.
        Assert.Equal(JsonValueKind.Number, data["precioTotal"]!.GetValueKind());
        Assert.Equal(1428.00m, data["precioTotal"]!.GetValue<decimal>());
    }
}
