using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaVersionHotel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Version",
            table: "Hoteles",
            type: "int",
            nullable: false,
            defaultValue: 0);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Version",
            table: "Hoteles");
    }
}
