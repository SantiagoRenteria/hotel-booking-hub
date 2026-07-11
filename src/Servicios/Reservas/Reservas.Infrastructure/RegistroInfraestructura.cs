using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Infrastructure.Cache;
using Reservas.Infrastructure.Disponibilidad;
using Reservas.Infrastructure.Idempotencia;
using StackExchange.Redis;
using Reservas.Infrastructure.Mensajeria;
using Reservas.Infrastructure.Outbox;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;

namespace Reservas.Infrastructure;

/// <summary>Registro de los adaptadores de infraestructura del BC de Reservas.</summary>
public static class RegistroInfraestructura
{
    public static IServiceCollection AddReservasInfrastructure(this IServiceCollection servicios, string? cadenaConexion)
    {
        servicios.AddDbContext<ReservasDbContext>(opciones => opciones.UseSqlServer(cadenaConexion));

        // Escritura atómica: repositorio de staging + unidad de trabajo transaccional + cola de outbox +
        // TransactionBehavior (eslabón interno del pipeline, solo comandos). MessageId scoped por petición.
        servicios.AddScoped<IReservaRepository, ReservaRepository>();
        servicios.AddScoped<EjecutorTransaccional>();
        servicios.AddScoped<ContextoMensajeria>();
        servicios.AddScoped<IColaOutbox, ColaOutbox>();
        servicios.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Consumidor de eventos de catálogo (E3): proyecta a ProyeccionHabitacion con inbox de idempotencia.
        servicios.AddScoped<IConsumidorEventosCatalogo, ProyectorCatalogo>();

        // Lectura de disponibilidad (Story 3.2): buscador SQL sobre el read-model + decoradora de caché Redis.
        // La caché (singleton, sobre IDistributedCache) hace de lector y de invalidador por ciudad.
        servicios.AddSingleton<OpcionesCacheDisponibilidad>();
        servicios.AddSingleton<CacheDisponibilidadRedis>();
        servicios.AddSingleton<ICacheDisponibilidad>(sp => sp.GetRequiredService<CacheDisponibilidadRedis>());
        servicios.AddSingleton<IInvalidadorCacheDisponibilidad>(sp => sp.GetRequiredService<CacheDisponibilidadRedis>());
        servicios.AddScoped<BuscadorDisponibilidadSql>();
        servicios.AddScoped<IBuscadorDisponibilidad>(sp => new BuscadorDisponibilidadCacheado(
            sp.GetRequiredService<BuscadorDisponibilidadSql>(),
            sp.GetRequiredService<ICacheDisponibilidad>()));

        // Lectura de reservas del agente (Story 3.3): query aislada server-side por AgenteEmail.
        servicios.AddScoped<ILectorReservasAgente, LectorReservasAgenteSql>();

        // Lectura de cancelaciones pendientes con antigüedad (Story 4.3): query aislada por AgenteEmail.
        servicios.AddScoped<ILectorCancelacionesPendientes, LectorCancelacionesPendientesSql>();

        // Relay del outbox (productor): procesador + BackgroundService que sondea y publica.
        servicios.AddSingleton<OpcionesRelayOutbox>();
        servicios.AddSingleton<ProcesadorOutbox>();
        servicios.AddHostedService<RelayOutbox>();

        servicios.AddSingleton<IDisponibilidadHabitacion, DisponibilidadHabitacionSembrada>();

        // Almacén de idempotencia de creación de reserva (Story 1.7, DOCUMENTO-BASE §8.5): SETNX+TTL en Redis si
        // hay cadena configurada (dedup entre instancias); si no, fallback en memoria (dev de instancia única).
        servicios.AddSingleton(new OpcionesIdempotenciaReserva());
        servicios.AddSingleton<IAlmacenIdempotenciaReserva>(sp =>
        {
            var cadenaRedis = sp.GetRequiredService<IConfiguration>().GetConnectionString("redis");
            return string.IsNullOrWhiteSpace(cadenaRedis)
                ? new AlmacenIdempotenciaReservaEnMemoria()
                : new AlmacenIdempotenciaReservaRedis(
                    ConnectionMultiplexer.Connect(cadenaRedis), sp.GetRequiredService<OpcionesIdempotenciaReserva>());
        });

        // Transporte de eventos por entorno (Story 9.1, ADR-019): si hay cadena de RabbitMQ (local/compose),
        // se publica de verdad al broker; si no (tests unit sin broker), se cae al placeholder que solo loguea.
        // El adaptador Dapr→Service Bus de nube se enchufa por este mismo seam en la Épica 8, sin tocar el dominio.
        servicios.AddSingleton<IPublicadorEventos>(sp =>
        {
            var cadenaRabbit = sp.GetRequiredService<IConfiguration>().GetConnectionString("rabbitmq");
            return string.IsNullOrWhiteSpace(cadenaRabbit)
                ? new PublicadorEventosLog(sp.GetRequiredService<ILogger<PublicadorEventosLog>>())
                : new PublicadorEventosRabbitMq(cadenaRabbit, sp.GetRequiredService<ILogger<PublicadorEventosRabbitMq>>());
        });

        return servicios;
    }
}
