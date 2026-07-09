using HotelBookingHub.Comun.Eventos;
using HotelBookingHub.Comun.Mensajeria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reservas.Application.Abstracciones;
using Reservas.Domain.Puertos;
using Reservas.Infrastructure.Cache;
using Reservas.Infrastructure.Disponibilidad;
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

        // Relay del outbox (productor): procesador + BackgroundService que sondea y publica.
        servicios.AddSingleton<OpcionesRelayOutbox>();
        servicios.AddSingleton<ProcesadorOutbox>();
        servicios.AddHostedService<RelayOutbox>();

        // Placeholders de 1.6a (reemplazados en E3 y por Dapr pub/sub respectivamente).
        servicios.AddSingleton<IDisponibilidadHabitacion, DisponibilidadHabitacionSembrada>();
        servicios.AddSingleton<IPublicadorEventos, PublicadorEventosLog>();

        return servicios;
    }
}
