using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaProyeccionEInbox : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MensajesProcesados",
            columns: table => new
            {
                MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProcesadoEn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MensajesProcesados", x => x.MessageId);
            });

        migrationBuilder.CreateTable(
            name: "ProyeccionHabitacion",
            columns: table => new
            {
                HabitacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HotelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Ciudad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                Tipo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Ubicacion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                Capacidad = table.Column<int>(type: "int", nullable: true),
                VersionEstatico = table.Column<int>(type: "int", nullable: true),
                CostoBase = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                Impuestos = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                VersionPrecio = table.Column<int>(type: "int", nullable: false),
                Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                VersionEstado = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProyeccionHabitacion", x => x.HabitacionId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProyeccionHabitacion_Ciudad",
            table: "ProyeccionHabitacion",
            column: "Ciudad");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MensajesProcesados");

        migrationBuilder.DropTable(
            name: "ProyeccionHabitacion");
    }
}
