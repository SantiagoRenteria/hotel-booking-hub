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
/// Story 1.7 (AC-E1.7.1..4) — `POST /api/v1/reservas` con `Idempotency-Key`. Se sustituye `ISender` por un fake que
/// CUENTA despachos y devuelve un `Id` DISTINTO por despacho: así "mismo Id en el replay" es un discriminante real
/// (no inerte). Cubre reintento (dedup), concurrencia (reservar-antes-de-despachar → 1 sola creación), claves
/// distintas (sin dedup cruzada), cuerpo distinto (422), fallo (la clave no se "quema") y sin header (sin cambios).
/// </summary>
public sealed class IdempotenciaReservaTests(ReservasApiFactory factory) : IClassFixture<ReservasApiFactory>
{
    private const string Ruta = "/api/v1/reservas";

    private sealed class SenderFake(Func<Result<ReservaResponseDto>>? fabrica = null) : ISender
    {
        public int Despachos;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct)
        {
            Interlocked.Increment(ref Despachos);
            var resultado = fabrica?.Invoke()
                ?? Result<ReservaResponseDto>.Ok(new ReservaResponseDto(Guid.CreateVersion7(), "Confirmada", 150m));
            return Task.FromResult((TResponse)(object)resultado);
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

    private HttpClient ClienteCon(SenderFake sender)
    {
        var cliente = factory.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<ISender>();
                s.AddSingleton<ISender>(sender);
            })).CreateClient();
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenDePrueba.Emitir(rol: TokenDePrueba.RolAgente));
        return cliente;
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
        var sender = new SenderFake();
        var cliente = ClienteCon(sender);
        var cuerpo = CuerpoReserva(Guid.CreateVersion7().ToString());

        var r1 = await cliente.SendAsync(Post(cuerpo, "clave-A"));
        var r2 = await cliente.SendAsync(Post(cuerpo, "clave-A"));

        // El comando se despachó UNA sola vez; el 2º se resolvió por replay con el MISMO Id (el fake daría otro Id
        // si hubiera re-despachado → la igualdad de Id es un discriminante real, no inerte).
        Assert.Equal(1, sender.Despachos);
        var dto1 = await r1.Content.ReadFromJsonAsync<ReservaResponseDto>();
        var dto2 = await r2.Content.ReadFromJsonAsync<ReservaResponseDto>();
        Assert.Equal(dto1!.Id, dto2!.Id);
    }

    [Fact]
    public async Task Concurrencia_misma_clave_crea_una_sola_reserva()
    {
        var sender = new SenderFake();
        var cliente = ClienteCon(sender);
        var cuerpo = CuerpoReserva(Guid.CreateVersion7().ToString());

        // Dos POST concurrentes con la misma clave (doble-clic real): reservar-antes-de-despachar garantiza que solo
        // uno despacha; el otro obtiene replay o 409 "en proceso", pero NUNCA una segunda creación.
        var t1 = cliente.SendAsync(Post(cuerpo, "clave-C"));
        var t2 = cliente.SendAsync(Post(cuerpo, "clave-C"));
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, sender.Despachos);
    }

    [Fact]
    public async Task Claves_distintas_no_deduplican()
    {
        var sender = new SenderFake();
        var cliente = ClienteCon(sender);

        await cliente.SendAsync(Post(CuerpoReserva(Guid.CreateVersion7().ToString()), "clave-D1"));
        await cliente.SendAsync(Post(CuerpoReserva(Guid.CreateVersion7().ToString()), "clave-D2"));

        // Claves distintas → cada una sigue su curso, sin dedup cruzada (AC-E1.7.2).
        Assert.Equal(2, sender.Despachos);
    }

    [Fact]
    public async Task Misma_clave_cuerpo_distinto_responde_422()
    {
        var sender = new SenderFake();
        var cliente = ClienteCon(sender);

        var r1 = await cliente.SendAsync(Post(CuerpoReserva(Guid.CreateVersion7().ToString()), "clave-E"));
        var r2 = await cliente.SendAsync(Post(CuerpoReserva(Guid.CreateVersion7().ToString()), "clave-E"));

        Assert.True(r1.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r2.StatusCode);
    }

    [Fact]
    public async Task Creacion_fallida_no_quema_la_clave()
    {
        // El comando falla (p. ej. overbooking → 409): la clave se libera, así que un reintento vuelve a despachar.
        var sender = new SenderFake(() => Result<ReservaResponseDto>.Conflicto("La habitación ya está reservada"));
        var cliente = ClienteCon(sender);
        var cuerpo = CuerpoReserva(Guid.CreateVersion7().ToString());

        var r1 = await cliente.SendAsync(Post(cuerpo, "clave-F"));
        var r2 = await cliente.SendAsync(Post(cuerpo, "clave-F"));

        Assert.Equal(HttpStatusCode.Conflict, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
        Assert.Equal(2, sender.Despachos); // la clave NO quedó bloqueada por el fallo.
    }

    [Fact]
    public async Task Sin_header_no_deduplica()
    {
        var sender = new SenderFake();
        var cliente = ClienteCon(sender);
        var cuerpo = CuerpoReserva(Guid.CreateVersion7().ToString());

        await cliente.SendAsync(Post(cuerpo, idempotencyKey: null));
        await cliente.SendAsync(Post(cuerpo, idempotencyKey: null));

        Assert.Equal(2, sender.Despachos);
    }
}
