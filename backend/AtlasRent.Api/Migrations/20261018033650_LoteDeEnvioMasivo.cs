using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class LoteDeEnvioMasivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lote_id",
                table: "envio",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_envio_lote",
                table: "envio",
                column: "lote_id");

            // F4-03. Sin DELETE, como todas las de F1 en adelante: nada se borra nunca (AGENTS.md §6).
            // El lote no es una tabla, así que no hay ninguna tabla nueva que conceder: `lote_id` es
            // una columna de `envio` y hereda sus permisos. El GRANT va igual y sin `DELETE`, para que
            // quede escrito que el envío masivo no abre ninguna puerta nueva.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON envio TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_envio_lote",
                table: "envio");

            migrationBuilder.DropColumn(
                name: "lote_id",
                table: "envio");
        }
    }
}
