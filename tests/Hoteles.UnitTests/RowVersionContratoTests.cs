using System.Text.Json;
using Hoteles.Application.Hoteles;
using Hoteles.Application.Hoteles.EditarHotel;

namespace Hoteles.UnitTests;

/// <summary>
/// Contrato del token de concurrencia (party-mode · Murat): el <c>rowVersion</c> viaja como base64 en JSON y
/// debe hacer round-trip SIN pérdida — si el base64 que devuelve el DTO no vuelve a ser el mismo <c>byte[]</c>,
/// la concurrencia optimista es teatro (un token corrupto nunca coincidiría o coincidiría por accidente).
/// </summary>
public sealed class RowVersionContratoTests
{
    // Bytes con casos peliagudos de base64: 0x00, 0xFF, alto/bajo, longitud no múltiplo de 3 (padding).
    private static readonly byte[] _bytes = [0x00, 0x01, 0x02, 0xFA, 0xFF, 0x80, 0x40, 0x08];

    [Fact]
    public void RowVersion_del_comando_hace_round_trip_json_sin_perdida()
    {
        var original = new EditarHotelCommand(Guid.CreateVersion7(), _bytes, "Hotel", "Ciudad", "Dir", "Desc");

        var json = JsonSerializer.Serialize(original);
        var deserializado = JsonSerializer.Deserialize<EditarHotelCommand>(json)!;

        Assert.Equal(_bytes, deserializado.RowVersion); // byte[] → base64 en JSON → byte[] idéntico
    }

    [Fact]
    public void RowVersion_base64_del_dto_reenviado_en_el_comando_recupera_los_bytes()
    {
        // El DTO expone el rowVersion como base64 (lo que devolvió el handler tras guardar).
        var dto = new HotelResponseDto(Guid.CreateVersion7(), "Hotel", "Ciudad", "Habilitado", Convert.ToBase64String(_bytes));

        // El cliente lo reenvía tal cual como el campo rowVersion (byte[]) del siguiente comando, vía JSON.
        var json = $$"""{"id":"{{Guid.CreateVersion7()}}","rowVersion":"{{dto.RowVersion}}","nombre":"H","ciudad":"C","direccion":"D","descripcion":"X"}""";
        var comando = JsonSerializer.Deserialize<EditarHotelCommand>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(_bytes, comando.RowVersion); // base64 del DTO → byte[] del comando, sin pérdida
    }
}
