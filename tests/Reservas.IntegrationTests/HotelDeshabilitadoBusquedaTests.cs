using System.Text.Json;
using HotelBookingHub.Comun.Eventos;
using Reservas.Application.Reservas.BuscarDisponibilidad;
using Reservas.Infrastructure.Persistencia;
using Reservas.Infrastructure.Proyeccion;
using Xunit;

namespace Reservas.IntegrationTests;

/// <summary>
/// Story 3.2 (AC-E3.2.2/FR-7) — las habitaciones de un hotel deshabilitado NO se ofertan. El estado del hotel se
/// materializa en una dimensión independiente (<see cref="ProyeccionHotelEstado"/>) alimentada por eventos reales
/// procesados por el <see cref="ProyectorCatalogo"/> (sin sembrar el estado a mano). Cubre desorden (evento antes
/// del alta; evento viejo por versión) y ortogonalidad (rehabilitar el hotel NO resucita habitaciones
/// deshabilitadas individualmente). Ciudad única por test para aislar.
/// </summary>
[Collection("sqlserver")]
public sealed class HotelDeshabilitadoBusquedaTests(SqlServerFixture fixture)
{
    private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly _entrada = new(2026, 12, 10);
    private static readonly DateOnly _salida = new(2026, 12, 12);

    private static string CiudadUnica() => "C" + Guid.NewGuid().ToString("N")[..12];

    private static EventoIntegracion Envelope(string tipo, int version, object data)
    {
        var e = new EventoIntegracion(Guid.CreateVersion7(), tipo, version,
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), null, data);
        return JsonSerializer.Deserialize<EventoIntegracion>(JsonSerializer.Serialize(e, _web), _web)!;
    }

    private async Task<Guid> SembrarHabitacionAsync(Guid hotelId, string ciudad, string estado = "Habilitada")
    {
        var habId = Guid.CreateVersion7();
        await using var db = fixture.CrearContexto();
        var fila = ProyeccionHabitacion.Nueva(habId);
        fila.AplicarAgregada(hotelId, ciudad, "Suite", "Piso 3", 4, 100m, 19m, estado, 1);
        db.ProyeccionesHabitacion.Add(fila);
        await db.SaveChangesAsync();
        return habId;
    }

    private async Task ProcesarAsync(EventoIntegracion evento)
    {
        await using var db = fixture.CrearContexto();
        await new ProyectorCatalogo(db).ProcesarAsync(evento, CancellationToken.None);
    }

    private async Task<IReadOnlyList<HabitacionDisponibleDto>> BuscarAsync(string ciudad)
    {
        await using var db = fixture.CrearContexto();
        return await new BuscadorDisponibilidadSql(db)
            .BuscarAsync(new BuscarDisponibilidadQuery(ciudad, _entrada, _salida, 2), CancellationToken.None);
    }

    // AC-E3.2.2 — deshabilitar el hotel (evento real) excluye TODAS sus habitaciones.
    [Fact]
    public async Task Hotel_deshabilitado_excluye_sus_habitaciones()
    {
        var ciudad = CiudadUnica();
        var hotel = Guid.CreateVersion7();
        await SembrarHabitacionAsync(hotel, ciudad);
        await SembrarHabitacionAsync(hotel, ciudad);
        Assert.Equal(2, (await BuscarAsync(ciudad)).Count);

        await ProcesarAsync(Envelope(HotelDeshabilitadoV1.Tipo, 1, new HotelDeshabilitadoV1(hotel, ciudad)));

        Assert.Empty(await BuscarAsync(ciudad));
    }

    // Ortogonalidad — rehabilitar el hotel devuelve las habitaciones, MENOS la deshabilitada individualmente.
    [Fact]
    public async Task Rehabilitar_hotel_no_resucita_habitacion_deshabilitada_individualmente()
    {
        var ciudad = CiudadUnica();
        var hotel = Guid.CreateVersion7();
        var activa = await SembrarHabitacionAsync(hotel, ciudad, "Habilitada");
        await SembrarHabitacionAsync(hotel, ciudad, "Deshabilitada"); // apagada por sí misma

        await ProcesarAsync(Envelope(HotelDeshabilitadoV1.Tipo, 1, new HotelDeshabilitadoV1(hotel, ciudad)));
        Assert.Empty(await BuscarAsync(ciudad));

        await ProcesarAsync(Envelope(HotelHabilitadoV1.Tipo, 2, new HotelHabilitadoV1(hotel, ciudad)));

        var resultado = await BuscarAsync(ciudad);
        Assert.Equal(activa, Assert.Single(resultado).HabitacionId); // la individualmente deshabilitada sigue fuera
    }

    // Desorden — HotelDeshabilitado llega ANTES del alta de la habitación: al llegar el alta, sigue excluida.
    [Fact]
    public async Task Hotel_deshabilitado_antes_del_alta_igual_excluye()
    {
        var ciudad = CiudadUnica();
        var hotel = Guid.CreateVersion7();

        await ProcesarAsync(Envelope(HotelDeshabilitadoV1.Tipo, 1, new HotelDeshabilitadoV1(hotel, ciudad)));
        await SembrarHabitacionAsync(hotel, ciudad); // el alta llega DESPUÉS

        Assert.Empty(await BuscarAsync(ciudad));
    }

    // LWW — un HotelDeshabilitado viejo por versión no revierte un HotelHabilitado más nuevo.
    [Fact]
    public async Task Evento_de_hotel_viejo_no_revierte_el_estado()
    {
        var ciudad = CiudadUnica();
        var hotel = Guid.CreateVersion7();
        var hab = await SembrarHabitacionAsync(hotel, ciudad);

        await ProcesarAsync(Envelope(HotelHabilitadoV1.Tipo, 2, new HotelHabilitadoV1(hotel, ciudad)));
        await ProcesarAsync(Envelope(HotelDeshabilitadoV1.Tipo, 1, new HotelDeshabilitadoV1(hotel, ciudad))); // viejo

        Assert.Equal(hab, Assert.Single(await BuscarAsync(ciudad)).HabitacionId);
    }
}
