using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class CancelacionDeIncrementos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F2-06: la cancelación de un incremento. Un incremento no se borra (AGENTS.md §6): se
            // marca, y la fila se queda con su fecha para poder responder por qué no llegó a subir
            // la renta. No hace falta GRANT aquí: los permisos son de tabla, no de columna, y la
            // migración anterior ya le dio a rent_app SELECT, INSERT y UPDATE sobre
            // contrato_incremento.
            migrationBuilder.AddColumn<bool>(
                name: "cancelado",
                table: "contrato_incremento",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cancelado_at",
                table: "contrato_incremento",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cancelado",
                table: "contrato_incremento");

            migrationBuilder.DropColumn(
                name: "cancelado_at",
                table: "contrato_incremento");
        }
    }
}
