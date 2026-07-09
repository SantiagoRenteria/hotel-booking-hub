using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class InicialHoteles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Hoteles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Ciudad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                Descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                Seq = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Hoteles", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Hoteles_Seq",
            table: "Hoteles",
            column: "Seq",
            unique: true)
            .Annotation("SqlServer:Clustered", true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Hoteles");
    }
}
