using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hoteles.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaVersionYOutboxHabitaciones : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Version",
            table: "Habitaciones",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "OutboxMessages",
            columns: table => new
            {
                Seq = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Intentos = table.Column<int>(type: "int", nullable: false),
                ReclamadoEn = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Seq);
            });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_Estado_Seq",
            table: "OutboxMessages",
            columns: new[] { "Estado", "Seq" });

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_MessageId",
            table: "OutboxMessages",
            column: "MessageId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "OutboxMessages");

        migrationBuilder.DropColumn(
            name: "Version",
            table: "Habitaciones");
    }
}
