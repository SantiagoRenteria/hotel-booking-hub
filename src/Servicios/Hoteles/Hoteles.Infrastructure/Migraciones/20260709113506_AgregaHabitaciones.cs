using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaHabitaciones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Habitaciones",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HotelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CostoBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Impuestos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Ubicacion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                Seq = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Habitaciones", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Habitaciones_HotelId",
            table: "Habitaciones",
            column: "HotelId");

        migrationBuilder.CreateIndex(
            name: "IX_Habitaciones_Seq",
            table: "Habitaciones",
            column: "Seq",
            unique: true)
            .Annotation("SqlServer:Clustered", true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Habitaciones");
    }
}
