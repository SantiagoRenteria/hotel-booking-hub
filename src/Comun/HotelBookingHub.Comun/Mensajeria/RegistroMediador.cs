using System.Reflection;
using FluentValidation;
using HotelBookingHub.Comun.Mensajeria.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace HotelBookingHub.Comun.Mensajeria;

/// <summary>
/// Registro único del pipeline del mediator por servicio (ADR-018). Handlers y validators se
/// descubren por scan del assembly de aplicación; los behaviors se registran en el orden canónico literal.
/// </summary>
public static class RegistroMediador
{
    public static IServiceCollection AddMediatorPipeline(this IServiceCollection servicios, Assembly ensambladoAplicacion)
    {
        servicios.AddScoped<ISender, Sender>();

        // Orden canónico: el primero registrado envuelve más afuera (Logging → Validation → Handler).
        // El TransactionBehavior (transacción + outbox + retry 1205) se inserta en 1.6b.
        servicios.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        servicios.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        RegistrarImplementaciones(servicios, ensambladoAplicacion, typeof(IRequestHandler<,>));
        RegistrarImplementaciones(servicios, ensambladoAplicacion, typeof(IValidator<>));

        return servicios;
    }

    private static void RegistrarImplementaciones(IServiceCollection servicios, Assembly ensamblado, Type interfazAbierta)
    {
        foreach (var tipo in ensamblado.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var interfazCerrada in tipo.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == interfazAbierta))
            {
                servicios.AddScoped(interfazCerrada, tipo);
            }
        }
    }
}
