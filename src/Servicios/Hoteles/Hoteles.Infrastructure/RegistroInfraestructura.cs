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

        // Emisión transaccional de eventos de catálogo (2.5, Opción U): la cola stagea la fila de outbox en el
        // mismo DbContext; el repositorio la confirma junto al dominio en un único SaveChanges (sin TransactionBehavior).
        servicios.AddScoped<IColaOutbox, ColaOutbox>();

        // Relay del outbox (productor): procesador + BackgroundService que sondea y publica.
        servicios.AddSingleton<OpcionesRelayOutbox>();
        servicios.AddSingleton<ProcesadorOutbox>();
        servicios.AddHostedService<RelayOutbox>();

        // Transporte de eventos por entorno (Story 9.1, ADR-019): RabbitMQ si hay cadena (local/compose), si no
        // el placeholder que solo loguea. El adaptador Dapr→Service Bus de nube usa este mismo seam (Épica 8).
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
