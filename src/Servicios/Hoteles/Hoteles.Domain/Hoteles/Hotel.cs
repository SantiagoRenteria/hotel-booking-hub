namespace Hoteles.Domain.Hoteles;

/// <summary>
/// Aggregate root del catálogo del agente. Identidad <b>UUID v7</b> (PK no-clustered; la clustering key
/// <c>Seq</c> es shadow property en infraestructura — ADR-017). Setters privados; se crea por
/// <see cref="Crear"/> y muta por <see cref="Editar"/> / <see cref="Eliminar"/>, que garantizan los invariantes
/// obligatorios (nombre y ciudad no vacíos). La baja es <b>lógica</b> (<see cref="Eliminado"/>): no hay borrado
/// físico; el <c>HotelesDbContext</c> filtra los eliminados con un query filter global.
/// </summary>
public sealed class Hotel
{
    public Guid Id { get; private set; }

    /// <summary>
    /// Agente propietario del hotel (Story 6.3, aislamiento entre agentes): el agente que lo creó, en forma
    /// canónica (minúsculas + trim). Es identidad, <b>inmutable</b> tras el alta (se fija en <see cref="Crear"/>;
    /// ni <see cref="Editar"/> ni el ciclo de vida lo tocan). El aislamiento se aplica de forma centralizada por
    /// query filter en <c>HotelesDbContext</c>: un hotel de otro agente es invisible → carga-para-mutar → 404.
    /// </summary>
    public string AgentePropietario { get; private set; } = string.Empty;

    public string Nombre { get; private set; } = string.Empty;
    public string Ciudad { get; private set; } = string.Empty;
    public string Direccion { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public EstadoHotel Estado { get; private set; }

    /// <summary>Baja lógica (soft delete): un hotel eliminado deja de aparecer en consultas y de ofertar.</summary>
    public bool Eliminado { get; private set; }

    /// <summary>
    /// Versión de secuencia del estado del hotel (Story 3.2): parte monotónica del order key
    /// <c>(HotelId, Version)</c> de los eventos <c>HotelHabilitado</c>/<c>HotelDeshabilitado</c>. Se incrementa en
    /// CADA transición efectiva de estado (habilitar y deshabilitar), no en las idempotentes, de modo que el
    /// consumidor (proyección de Reservas) resuelve el desorden por LWW. Independiente del <c>rowversion</c>.
    /// </summary>
    public int Version { get; private set; }

    private Hotel() { } // EF Core

    public static Hotel Crear(string nombre, string ciudad, string direccion, string descripcion, EstadoHotel estado, string agentePropietario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciudad);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentePropietario);

        return new Hotel
        {
            Id = Guid.CreateVersion7(),
            // Canónico (minúsculas + trim): el aislamiento no depende del collation de SQL (patrón de Reservas 3.3).
            AgentePropietario = agentePropietario.Trim().ToLowerInvariant(),
            Nombre = nombre.Trim(),
            Ciudad = ciudad.Trim(),
            Direccion = (direccion ?? string.Empty).Trim(),
            Descripcion = (descripcion ?? string.Empty).Trim(),
            Estado = estado,
        };
    }

    /// <summary>
    /// Actualiza los datos descriptivos del hotel (AC-E2.2.1). NO cambia el <see cref="Estado"/>: la transición
    /// del ciclo de vida (habilitar/deshabilitar) es una operación dedicada con sus propias reglas (Story 2.3),
    /// no un set silencioso del CRUD. Tampoco toca sus habitaciones (independencia).
    /// </summary>
    public void Editar(string nombre, string ciudad, string direccion, string descripcion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentException.ThrowIfNullOrWhiteSpace(ciudad);

        Nombre = nombre.Trim();
        Ciudad = ciudad.Trim();
        Direccion = (direccion ?? string.Empty).Trim();
        Descripcion = (descripcion ?? string.Empty).Trim();
    }

    /// <summary>Da de baja lógica el hotel (AC-E2.2.2): marca <see cref="Eliminado"/> sin borrado físico.</summary>
    public void Eliminar() => Eliminado = true;

    /// <summary>
    /// Habilita el hotel (AC-E2.3.1). Idempotente. Devuelve <c>true</c> si cambió el estado; en ese caso emite
    /// <c>HotelHabilitado</c> e incrementa la <see cref="Version"/> (Story 3.2). Junto con <see cref="Deshabilitar"/>
    /// es la ÚNICA vía de transición del ciclo de vida: la edición (2.2) no toca el estado.
    /// </summary>
    public bool Habilitar()
    {
        if (Estado == EstadoHotel.Habilitado)
        {
            return false;
        }

        Estado = EstadoHotel.Habilitado;
        Version++;
        return true;
    }

    /// <summary>
    /// Deshabilita el hotel (AC-E2.3.1): deja de ofertarse. Idempotente. Devuelve <c>true</c> si cambió el estado;
    /// en ese caso emite <c>HotelDeshabilitado</c> e incrementa la <see cref="Version"/> (Story 3.2, AC-E3.2.2).
    /// </summary>
    public bool Deshabilitar()
    {
        if (Estado == EstadoHotel.Deshabilitado)
        {
            return false;
        }

        Estado = EstadoHotel.Deshabilitado;
        Version++;
        return true;
    }
}
