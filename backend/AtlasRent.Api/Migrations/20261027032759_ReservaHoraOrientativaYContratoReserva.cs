using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReservaHoraOrientativaYContratoReserva : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hora_orientativa",
                table: "reserva_dia",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            // BUG-027: las horas pasan a ser un texto orientativo; las existentes se conservan como texto.
            migrationBuilder.Sql(
                "UPDATE reserva_dia SET hora_orientativa = to_char(hora_entrada, 'HH24:MI') || ' a ' || to_char(hora_salida, 'HH24:MI');");

            migrationBuilder.DropColumn(
                name: "hora_entrada",
                table: "reserva_dia");

            migrationBuilder.DropColumn(
                name: "hora_salida",
                table: "reserva_dia");

            migrationBuilder.AddColumn<Guid>(
                name: "reserva_id",
                table: "contrato",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contrato_reserva_id",
                table: "contrato",
                column: "reserva_id");

            migrationBuilder.AddForeignKey(
                name: "fk_contrato_reserva_reserva_id",
                table: "contrato",
                column: "reserva_id",
                principalTable: "reserva",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_contrato_reserva_reserva_id",
                table: "contrato");

            migrationBuilder.DropIndex(
                name: "ix_contrato_reserva_id",
                table: "contrato");

            migrationBuilder.DropColumn(
                name: "hora_orientativa",
                table: "reserva_dia");

            migrationBuilder.DropColumn(
                name: "reserva_id",
                table: "contrato");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_entrada",
                table: "reserva_dia",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "hora_salida",
                table: "reserva_dia",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(0, 0, 0));
        }
    }
}
