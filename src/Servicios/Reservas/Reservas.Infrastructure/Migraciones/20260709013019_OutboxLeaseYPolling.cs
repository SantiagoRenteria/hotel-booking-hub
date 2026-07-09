using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class OutboxLeaseYPolling : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ReclamadoEn",
            table: "OutboxMessages",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Estado_Seq",
            table: "OutboxMessages",
            columns: new[] { "Estado", "Seq" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OutboxMessages_Estado_Seq",
            table: "OutboxMessages");

        migrationBuilder.DropColumn(
            name: "ReclamadoEn",
            table: "OutboxMessages");
    }
}
