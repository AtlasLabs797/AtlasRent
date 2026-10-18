using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class PropuestasDelAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F6-03: `solicitud_aprobacion` deja de ser solo de contratos. Lo que se pide aprobar son
            // contratos (tipo 1) o una acción del asistente (tipo 2), y `contrato_id` se queda vacío
            // en el segundo caso: lo que hay que decidir va en `payload_json` y lo que hay que mirar
            // para que siga valiendo, en `huella_contenido`, igual que en un contrato.
            migrationBuilder.AlterColumn<Guid>(
                name: "contrato_id",
                table: "solicitud_aprobacion",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "payload_json",
                table: "solicitud_aprobacion",
                type: "text",
                nullable: true);

            // 1 y no 0: las solicitudes que ya había son todas de contrato, y un 0 las dejaría fuera
            // de la bandeja sin que nadie hubiera hecho nada.
            migrationBuilder.AddColumn<int>(
                name: "tipo",
                table: "solicitud_aprobacion",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payload_json",
                table: "solicitud_aprobacion");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "solicitud_aprobacion");

            migrationBuilder.AlterColumn<Guid>(
                name: "contrato_id",
                table: "solicitud_aprobacion",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
