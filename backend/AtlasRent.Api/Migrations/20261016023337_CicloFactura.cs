using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class CicloFactura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F3-08: las marcas de la anulación y de la primera modificación, en la factura y en la
            // asignación de cobro. No hay tablas nuevas, así que no hay GRANT nuevo: factura y
            // cobro_asignacion ya tienen SELECT, INSERT y UPDATE para rent_app desde su migración, que
            // es justo lo que necesitan estas operaciones. DELETE sigue sin conceder en ninguna de las
            // dos (AGENTS.md §6): anular y dejar sin vigor no borran nada.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anulado_at",
                table: "factura",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "anulado_por",
                table: "factura",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "modificada_at",
                table: "factura",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "modificada_por",
                table: "factura",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_anulacion",
                table: "factura",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "anulada_at",
                table: "cobro_asignacion",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "anulada_por",
                table: "cobro_asignacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_anulacion",
                table: "cobro_asignacion",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "anulado_at",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "anulado_por",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "modificada_at",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "modificada_por",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "motivo_anulacion",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "anulada_at",
                table: "cobro_asignacion");

            migrationBuilder.DropColumn(
                name: "anulada_por",
                table: "cobro_asignacion");

            migrationBuilder.DropColumn(
                name: "motivo_anulacion",
                table: "cobro_asignacion");
        }
    }
}
