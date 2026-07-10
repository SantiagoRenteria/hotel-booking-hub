using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AislamientoAgentePropietarioHotel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AgentePropietario",
            table: "Hoteles",
            type: "nvarchar(320)",
            maxLength: 320,
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AgentePropietario",
            table: "Hoteles");
    }
}
