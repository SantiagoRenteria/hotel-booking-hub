using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HotelBookingHub.Comun.Mensajeria;
using HotelBookingHub.Comun.Resultados;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reservas.Application.Reservas.CrearReserva;
using TestKit.Auth;

namespace Reservas.FunctionalTests;

/// <summary>
/// Story 1.7 (AC-E1.7.1..4) — `POST /api/v1/reservas` con header `Idempotency-Key`: reenviar el mismo request no
/// crea una segunda reserva (mismo `Id`, el comando se despacha UNA vez); misma clave + cuerpo distinto → `422`;
/// sin header → comportamiento actual. Se sustituye `ISender` por un fake que CUENTA despachos (sin BD real): el
/// discriminante de la dedup es el conteo de despachos, no solo el `Id`.
/// </summary>
public sealed class IdempotenciaReservaTests(ReservasApiFactory factory) : IClassFixture<ReservasApiFactory>
{
    private const string Ruta = "/api/v1/reservas";

    private sealed class SenderFake : ISender
    {
        public int Despachos;
        public static readonly Guid IdFijo = Guid.Parse("018f0000-0000-7000-8000-000000000001");

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct)
        {
            Interlocked.Increment(ref Despachos);
            object resultado = Result<ReservaResponseDto>.Ok(new ReservaResponseDto(IdFijo, "Confirmada", 150m));
            return Task.FromResult((TResponse)resultado);
        }
    }

    private static object CuerpoReserva(string habitacionId) => new
    {
        habitacionId,
        entrada = "2026-08-01",
        salida = "2026-08-03",
        huespedes = new[]
        {
            new
            {
                nombres = "Ana", apellidos = "López", fechaNacimiento = "1990-01-01", genero = "F",
                tipoDocumento = "CC", numeroDocumento = "123", email = "ana@x.com", telefono = "555",
            },
        },
        contactoEmergencia = new { nombreCompleto = "Juan Pérez", telefono = "555" },
        agenteEmail = "agente@x.com",
    };

    private (HttpClient cliente, SenderFake sender) CrearClienteConSenderFake()
    {
        var sender = new SenderFake();
        var cliente = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ISender>();
                s.AddSingleton<ISender>(sender);
            })).CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDePrueba.Emitir(rol: TokenDePrueba.RolAgente));
        return (cliente, sender);
    }

    private static HttpRequestMessage Post(object cuerpo, string? idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Ruta) { Content = JsonContent.Create(cuerpo) };
        if (idempotencyKey is not null)
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return req;
    }

    [Fact]
    public async Task Reintento_misma_clave_mismo_cuerpo_no_crea_segunda_reserva()
    {
        var (cliente, sender) = CrearClienteConSenderFake();
        var cuerpo = CuerpoReserva(Guid.CreateVersion7().ToString());

        var r1 = await cliente.SendAsync(Post(cuerpo, "clave-A"));
        var r2 = await cliente.SendAsync(Post(cuerpo, "clave-A"));

        // El comando se despachó UNA sola vez (el 2º request se resolvió desde el almacén de idempotencia).
        Assert.Equal(1, sender.Despachos);
        Assert.True(r1.IsSuccessStatusCode);
        Assert.True(r2.IsSuccessStatusCode);
        var dto1 = await r1.Content.ReadFromJsonAsync<ReservaResponseDto>();
        var dto2 = await r2.Content.ReadFromJsonAsync<ReservaResponseDto>();
        Assert.Equal(dto1!.Id, dto2!.Id);
    }

    [Fact]
    public async Task Misma_clave_cuerpo_distinto_responde_422()
    {
        var (cliente, _) = CrearClienteConSenderFake();

        var r1 = await cliente.SendAsync(Post(CuerpoReserva(Guid.CreateVersion7().ToString()), "clave-B"));
        var r2 = await cliente.SendAsync(Post(CuerpoReserva(Guid.CreateVersion7().ToString()), "clave-B"));

        Assert.True(r1.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
    }

    [Fact]
    public async Task Sin_header_no_deduplica()
    {
        var (cliente, sender) = CrearClienteConSenderFake();
        var cuerpo = CuerpoReserva(Guid.CreateVersion7().ToString());

        await cliente.SendAsync(Post(cuerpo, idempotencyKey: null));
        await cliente.SendAsync(Post(cuerpo, idempotencyKey: null));

        // Sin Idempotency-Key el comando se despacha en cada request (comportamiento actual, sin regresión).
        Assert.Equal(2, sender.Despachos);
    }
}
