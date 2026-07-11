using Dapr.Client;
using HotelBookingHub.Comun.Eventos;
using Hoteles.Application.Abstracciones;
using Hoteles.Domain.Puertos;
using Hoteles.Infrastructure.Mensajeria;
using Hoteles.Infrastructure.Outbox;
using Hoteles.Infrastructure.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hoteles.Infrastructure;

/// <summary>Registro de los adaptadores de infraestructura del BC de Hoteles.</summary>
public static class RegistroInfraestructura
{
    public static IServiceCollection AddHotelesInfrastructure(this IServiceCollection servicios, string? cadenaConexion)
    {
        servicios.AddDbContext<HotelesDbContext>(opciones => opciones.UseSqlServer(cadenaConexion));
        servicios.AddScoped<IHotelRepository, HotelRepository>();
        servicios.AddScoped<IHabitacionRepository, HabitacionRepository>();

        // Lectura del catálogo (Story T.5): read-port CQRS sobre el DbContext (hereda el query filter por agente).
        servicios.AddScoped<ILectorCatalogo, LectorCatalogoSql>();

        // Emisión transaccional de eventos de catálogo (2.5, Opción U): la cola stagea la fila de outbox en el
        // mismo DbContext; el repositorio la confirma junto al dominio en un único SaveChanges (sin TransactionBehavior).
        servicios.AddScoped<IColaOutbox, ColaOutbox>();

        // Relay del outbox (productor): procesador + BackgroundService que sondea y publica.
        servicios.AddSingleton<OpcionesRelayOutbox>();
        servicios.AddSingleton<ProcesadorOutbox>();
        servicios.AddHostedService<RelayOutbox>();

        // Transporte de eventos por entorno (Strategy, ADR-019): Dapr→Service Bus en NUBE (TransporteEventos=Dapr),
        // RabbitMQ directo si hay cadena (local/compose), y placeholder de log en tests. Todo tras el mismo puerto.
        if (string.Equals(Environment.GetEnvironmentVariable("TransporteEventos"), "Dapr", StringComparison.OrdinalIgnoreCase))
        {
            servicios.AddSingleton(new DaprClientBuilder().Build());
            servicios.AddSingleton<IPublicadorEventos, PublicadorEventosDapr>();
        }
        else
        {
            servicios.AddSingleton<IPublicadorEventos>(sp =>
            {
                var cadenaRabbit = sp.GetRequiredService<IConfiguration>().GetConnectionString("rabbitmq");
                return string.IsNullOrWhiteSpace(cadenaRabbit)
                    ? new PublicadorEventosLog(sp.GetRequiredService<ILogger<PublicadorEventosLog>>())
                    : new PublicadorEventosRabbitMq(cadenaRabbit, sp.GetRequiredService<ILogger<PublicadorEventosRabbitMq>>());
            });
        }

        return servicios;
    }
}
