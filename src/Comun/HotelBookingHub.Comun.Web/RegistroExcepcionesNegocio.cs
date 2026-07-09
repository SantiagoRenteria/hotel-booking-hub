using Microsoft.Extensions.DependencyInjection;

namespace HotelBookingHub.Comun.Web;

/// <summary>
/// Registro del manejo transversal de excepciones de negocio → Problem Details. Lo invocan todos los
/// <c>*.Api</c> para no duplicar el <c>AddProblemDetails</c>/<c>AddExceptionHandler</c> por servicio. En el
/// pipeline HTTP, cada API sigue llamando a <c>app.UseExceptionHandler()</c>.
/// </summary>
public static class RegistroExcepcionesNegocio
{
    public static IServiceCollection AddManejoExcepcionesNegocio(this IServiceCollection servicios)
    {
        servicios.AddProblemDetails();
        servicios.AddExceptionHandler<ManejadorExcepcionesNegocio>();
        return servicios;
    }
}
