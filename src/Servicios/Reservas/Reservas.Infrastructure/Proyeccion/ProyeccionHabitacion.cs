namespace Reservas.Infrastructure.Proyeccion;

/// <summary>
/// Read-model de catálogo de una habitación (Story 3.1), propiedad de Reservas, alimentado por los eventos de
/// catálogo de Hoteles (2.5). Es un CRDT de tres zonas con <b>field-ownership</b> (party-mode C) que converge bajo
/// reentrega y desorden — incluido un delta que llega antes del alta — sin buffer ni event-store:
/// <list type="bullet">
///   <item><b>Estáticos</b> (HotelId, Ciudad, Tipo, Ubicacion, Capacidad): dueño único = <c>HabitacionAgregada</c>.
///     Inmutables post-creación → <b>write-once</b> (se rellenan aunque el alta llegue tarde, sin retroceder nada).</item>
///   <item><b>Precio</b> (CostoBase, Impuestos): LWW por <see cref="VersionPrecio"/>.</item>
///   <item><b>Estado</b> (Estado): LWW por <see cref="VersionEstado"/>.</item>
/// </list>
/// <see cref="Hidratada"/> es false mientras no llegue el alta (fila parcial); la búsqueda de 3.2 filtra por ella.
/// <para><b>Frontera de diseño:</b> los campos estáticos son inmutables en el catálogo de Hoteles. Si un evento
/// futuro los mutara, deben migrar a una zona versionada (como precio/estado). Este read-model no cubre ese caso.</para>
/// </summary>
public sealed class ProyeccionHabitacion
{
    public Guid HabitacionId { get; private set; }

    // Zona estática (write-once; dueño: HabitacionAgregada).
    public Guid? HotelId { get; private set; }
    public string? Ciudad { get; private set; }
    public string? Tipo { get; private set; }
    public string? Ubicacion { get; private set; }
    public int? Capacidad { get; private set; }
    public int? VersionEstatico { get; private set; }

    // Zona precio (LWW por versión).
    public decimal? CostoBase { get; private set; }
    public decimal? Impuestos { get; private set; }
    public int VersionPrecio { get; private set; }

    // Zona estado (LWW por versión).
    public string? Estado { get; private set; }
    public int VersionEstado { get; private set; }

    /// <summary>True cuando ya llegó el alta (estáticos presentes). La búsqueda de 3.2 solo considera hidratadas.</summary>
    public bool Hidratada => VersionEstatico.HasValue;

    private ProyeccionHabitacion() { } // EF Core

    /// <summary>Fila nueva (posiblemente parcial: un delta pudo llegar antes del alta).</summary>
    public static ProyeccionHabitacion Nueva(Guid habitacionId) => new() { HabitacionId = habitacionId };

    /// <summary>
    /// Aplica <c>HabitacionAgregada</c>: siembra los estáticos (write-once) y, como valores iniciales, el precio
    /// y el estado — pero solo si ningún delta más nuevo ya ganó su bloque (guarda por versión).
    /// </summary>
    public void AplicarAgregada(
        Guid hotelId, string ciudad, string tipo, string ubicacion, int capacidad,
        decimal costoBase, decimal impuestos, string estado, int version)
    {
        if (VersionEstatico is null)
        {
            HotelId = hotelId;
            Ciudad = ciudad;
            Tipo = tipo;
            Ubicacion = ubicacion;
            Capacidad = capacidad;
            VersionEstatico = version;
        }

        if (version > VersionPrecio)
        {
            CostoBase = costoBase;
            Impuestos = impuestos;
            VersionPrecio = version;
        }

        if (version > VersionEstado)
        {
            Estado = estado;
            VersionEstado = version;
        }
    }

    /// <summary>Aplica <c>PrecioHabitacionCambiado</c>: LWW por <see cref="VersionPrecio"/> (descarta versiones viejas/repetidas).</summary>
    public void AplicarPrecio(decimal costoBase, decimal impuestos, int version)
    {
        if (version <= VersionPrecio)
        {
            return;
        }

        CostoBase = costoBase;
        Impuestos = impuestos;
        VersionPrecio = version;
    }

    /// <summary>Aplica <c>HabitacionDeshabilitada</c>: LWW por <see cref="VersionEstado"/> (no revive por eventos viejos).</summary>
    public void AplicarDeshabilitada(int version)
    {
        if (version <= VersionEstado)
        {
            return;
        }

        Estado = "Deshabilitada";
        VersionEstado = version;
    }
}
