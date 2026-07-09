using System.Text.Json;
using System.Text.Json.Nodes;
using HotelBookingHub.Comun.Eventos;
using Xunit;

namespace Contracts;

/// <summary>
/// Contract test (Story 4.1, Task 4): congela la FORMA del evento de integración
/// <c>SolicitudCancelacionRegistrada.v1</c> (envelope + data) que produce el BC de Reservas y consumirá E5.
/// Test de contrato PROPIO de eventos de reserva (separado del de catálogo). Un cambio incompatible en el
/// envelope, el semver del <c>type</c>, la order key (<c>data.aggregateId</c> + <c>version</c>) o las claves de
/// data rompe estas aserciones. Rápido, sin contenedores; corre en cada PR. La SEMÁNTICA no se prueba aquí.
/// </summary>
public sealed class ContratoSolicitudCancelacionRegistradaTests
{
    private static readonly JsonSerializerOptions _opciones = new(JsonSerializerDefaults.Web);

    private static EventoIntegracion EventoDeEjemplo() => new(
        Id: Guid.Parse("019f43ef-0000-7000-8000-000000000101"),
        Type: SolicitudCancelacionRegistradaV1.Tipo,
        Version: 1,
        OccurredAt: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
        TraceId: "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
        Data: new SolicitudCancelacionRegistradaV1(
            AggregateId: Guid.Parse("019f43ef-0000-7000-8000-0000000001aa"),
            Iniciador: "Viajero",
            MotivoCategoria: "CambioDePlanes",
            MotivoDetalle: "El viajero ya no puede asistir.",
            PenalidadPorcentaje: 100m,
            FechaSolicitud: new DateOnly(2026, 8, 20),
            HuespedEmail: "andres@example.com",
            AgenteEmail: "carolina@agencia.com"));

    [Fact]
    public void Envelope_conserva_sus_claves_y_el_type_lleva_semver()
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(EventoDeEjemplo(), _opciones))!.AsObject();

        Assert.Equal(
            new[] { "id", "type", "version", "occurredAt", "traceId", "data" }.OrderBy(k => k),
            root.Select(p => p.Key).OrderBy(k => k));

        Assert.True(Guid.TryParse((string?)root["id"], out var id) && id != Guid.Empty); // dedup key
        Assert.Equal("SolicitudCancelacionRegistrada.v1", (string?)root["type"]);
        Assert.Matches(@"^[A-Za-z]+\.v\d+$", (string?)root["type"]!);
        Assert.True(root["version"]!.GetValue<int>() >= 1); // order key (parte 2)
    }

    [Fact]
    public void Data_conserva_las_claves_congeladas_y_los_formatos()
    {
        var data = JsonNode.Parse(JsonSerializer.Serialize(EventoDeEjemplo(), _opciones))!["data"]!.AsObject();

        Assert.Equal(
            new[]
            {
                "aggregateId", "iniciador", "motivoCategoria", "motivoDetalle", "penalidadPorcentaje", "fechaSolicitud",
                "huespedEmail", "agenteEmail", // enriquecimiento aditivo Story 5.2 (destinatarios de la notificación)
            }.OrderBy(k => k),
            data.Select(p => p.Key).OrderBy(k => k));

        // Order key (parte 1) = ReservaId: aggregateId presente y no vacío.
        Assert.True(Guid.TryParse((string?)data["aggregateId"], out var agg) && agg != Guid.Empty);
        // DateOnly como yyyy-MM-dd.
        Assert.Equal("2026-08-20", (string?)data["fechaSolicitud"]);
        // Penalidad como número decimal, nunca string.
        Assert.Equal(JsonValueKind.Number, data["penalidadPorcentaje"]!.GetValueKind());
        Assert.Equal(100m, data["penalidadPorcentaje"]!.GetValue<decimal>());
        // Destinatarios de la notificación (Story 5.2): presentes en el contrato.
        Assert.Equal("andres@example.com", (string?)data["huespedEmail"]);
        Assert.Equal("carolina@agencia.com", (string?)data["agenteEmail"]);
    }

    [Fact]
    public void El_tipo_lleva_el_nombre_y_semver_esperados()
    {
        Assert.Equal("SolicitudCancelacionRegistrada.v1", SolicitudCancelacionRegistradaV1.Tipo);
    }

    [Fact]
    public void Compatibilidad_aditiva_un_payload_del_esquema_previo_deserializa_con_emails_null()
    {
        // Story 5.2: los emails se añadieron de forma ADITIVA. Un payload del esquema PREVIO (sin
        // huespedEmail/agenteEmail) debe seguir deserializando; los campos aditivos caen a null.
        const string jsonPrevio =
            """
            {
              "aggregateId": "019f43ef-0000-7000-8000-0000000001aa",
              "iniciador": "Viajero",
              "motivoCategoria": "CambioDePlanes",
              "motivoDetalle": "El viajero ya no puede asistir.",
              "penalidadPorcentaje": 100,
              "fechaSolicitud": "2026-08-20"
            }
            """;

        var data = JsonSerializer.Deserialize<SolicitudCancelacionRegistradaV1>(jsonPrevio, _opciones)!;

        Assert.Equal("Viajero", data.Iniciador);        // los campos previos siguen mapeando
        Assert.Equal(100m, data.PenalidadPorcentaje);
        Assert.Null(data.HuespedEmail);                 // los aditivos ausentes → null (no rompe)
        Assert.Null(data.AgenteEmail);
    }
}
