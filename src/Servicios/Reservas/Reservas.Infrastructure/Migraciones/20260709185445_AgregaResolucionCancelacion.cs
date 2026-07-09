using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaResolucionCancelacion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "CancelacionFechaResolucion",
            table: "Reservas",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancelacionMotivoResolucion",
            table: "Reservas",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CancelacionPenalidadAplicadaPorcentaje",
            table: "Reservas",
            type: "decimal(5,2)",
            precision: 5,
            scale: 2,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "CancelacionPenalidadFueOverride",
            table: "Reservas",
            type: "bit",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancelacionResueltaPor",
            table: "Reservas",
            type: "nvarchar(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancelacionResultado",
            table: "Reservas",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CancelacionFechaResolucion",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionMotivoResolucion",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionPenalidadAplicadaPorcentaje",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionPenalidadFueOverride",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionResueltaPor",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionResultado",
            table: "Reservas");
    }
}
