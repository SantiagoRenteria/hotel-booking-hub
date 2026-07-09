using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservas.Infrastructure.Migraciones;

/// <inheritdoc />
public partial class AgregaSolicitudCancelacion : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateOnly>(
            name: "CancelacionFechaSolicitud",
            table: "Reservas",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancelacionIniciadaPor",
            table: "Reservas",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancelacionMotivoCategoria",
            table: "Reservas",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CancelacionMotivoDetalle",
            table: "Reservas",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CancelacionPenalidadPorcentaje",
            table: "Reservas",
            type: "decimal(5,2)",
            precision: 5,
            scale: 2,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CancelacionFechaSolicitud",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionIniciadaPor",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionMotivoCategoria",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionMotivoDetalle",
            table: "Reservas");

        migrationBuilder.DropColumn(
            name: "CancelacionPenalidadPorcentaje",
            table: "Reservas");
    }
}
