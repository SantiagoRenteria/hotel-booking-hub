namespace Reservas.Domain;

/// <summary>
/// Ancla de ensamblado para escaneo por reflexión (NetArchTest, registro de handlers por assembly).
/// No tiene comportamiento; el dominio real (CalculadorPrecio, Estancia, Reserva…) llega desde la Story 1.4.
/// </summary>
public sealed class AssemblyReference
{
    private AssemblyReference() { }
}
