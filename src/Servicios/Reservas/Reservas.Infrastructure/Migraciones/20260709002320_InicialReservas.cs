using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class InicialReservas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
                Intentos = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OutboxMessages", x => x.Seq);
            });

        migrationBuilder.CreateTable(
            name: "Reservas",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HabitacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Entrada = table.Column<DateOnly>(type: "date", nullable: false),
                Salida = table.Column<DateOnly>(type: "date", nullable: false),
                Estado = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                Seq = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Reservas", x => x.Id)
                    .Annotation("SqlServer:Clustered", false);
            });

        migrationBuilder.CreateTable(
            name: "NochesHabitacion",
            columns: table => new
            {
                HabitacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Noche = table.Column<DateOnly>(type: "date", nullable: false),
                ReservaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NochesHabitacion", x => new { x.HabitacionId, x.Noche });
                table.ForeignKey(
                    name: "FK_NochesHabitacion_Reservas_ReservaId",
                    column: x => x.ReservaId,
                    principalTable: "Reservas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_NochesHabitacion_ReservaId",
            table: "NochesHabitacion",
            column: "ReservaId");

        migrationBuilder.CreateIndex(
            name: "IX_OutboxMessages_MessageId",
            table: "OutboxMessages",
            column: "MessageId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Reservas_Seq",
            table: "Reservas",
            column: "Seq",
            unique: true)
            .Annotation("SqlServer:Clustered", true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "NochesHabitacion");

        migrationBuilder.DropTable(
            name: "OutboxMessages");

        migrationBuilder.DropTable(
            name: "Reservas");
    }
}
