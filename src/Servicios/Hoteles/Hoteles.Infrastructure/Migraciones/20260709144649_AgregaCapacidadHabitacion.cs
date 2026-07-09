using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaCapacidadHabitacion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Capacidad",
            table: "Habitaciones",
            type: "int",
            nullable: false,
            defaultValue: 1); // invariante de dominio: Capacidad > 0 (backfill sensato; greenfield sin filas)
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Capacidad",
            table: "Habitaciones");
    }
}
