using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones
{
    /// <inheritdoc />
    public partial class AgregaProyeccionHotelEstado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProyeccionHotelEstado",
                columns: table => new
                {
                    HotelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    VersionEstado = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProyeccionHotelEstado", x => x.HotelId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProyeccionHotelEstado");
        }
    }
}
