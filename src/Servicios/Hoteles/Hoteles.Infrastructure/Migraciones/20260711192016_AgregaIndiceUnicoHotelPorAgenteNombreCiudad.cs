using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaIndiceUnicoHotelPorAgenteNombreCiudad : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Auto-sanado: crear un índice único sobre una tabla con duplicados PREEXISTENTES falla. En greenfield
        // (CI/Azure con BD fresca) no hay duplicados y esto es no-op; en una BD local reutilizada de corridas
        // previas (antes de la regla) puede haberlos. Se retira el exceso por BAJA LÓGICA (no borrado físico),
        // conservando el más antiguo (menor Seq) de cada grupo (AgentePropietario, Nombre, Ciudad) no eliminado.
        migrationBuilder.Sql(
            """
            ;WITH Duplicados AS (
                SELECT [Seq],
                       ROW_NUMBER() OVER (
                           PARTITION BY [AgentePropietario], [Nombre], [Ciudad]
                           ORDER BY [Seq]) AS rn
                FROM [Hoteles]
                WHERE [Eliminado] = 0
            )
            UPDATE h SET h.[Eliminado] = 1
            FROM [Hoteles] h
            INNER JOIN Duplicados d ON h.[Seq] = d.[Seq]
            WHERE d.rn > 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_Hoteles_AgentePropietario_Nombre_Ciudad",
            table: "Hoteles",
            columns: new[] { "AgentePropietario", "Nombre", "Ciudad" },
            unique: true,
            filter: "[Eliminado] = 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Hoteles_AgentePropietario_Nombre_Ciudad",
            table: "Hoteles");
    }
}
