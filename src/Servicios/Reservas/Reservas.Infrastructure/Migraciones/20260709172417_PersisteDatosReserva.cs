using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class PersisteDatosReserva : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AgenteEmail",
            table: "Reservas",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContactoNombreCompleto",
            table: "Reservas",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ContactoTelefono",
            table: "Reservas",
            type: "nvarchar(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PrecioTotal",
            table: "Reservas",
            type: "decimal(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.CreateTable(
            name: "ReservaHuespedes",
            columns: table => new
            {
                ReservaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Nombres = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Apellidos = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                FechaNacimiento = table.Column<DateOnly>(type: "date", nullable: false),
                Genero = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                DocumentoTipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                DocumentoNumero = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Telefono = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ReservaHuespedes", x => new { x.ReservaId, x.Id });
                table.ForeignKey(
                    name: "FK_ReservaHuespedes_Reservas_ReservaId",
                    column: x => x.ReservaId,
                    principalTable: "Reservas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ReservaHuespedes");

        migrationBuilder.DropColumn(
            name: "AgenteEmail",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "ContactoNombreCompleto",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "ContactoTelefono",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "PrecioTotal",
            table: "Reservas");
    }
}
