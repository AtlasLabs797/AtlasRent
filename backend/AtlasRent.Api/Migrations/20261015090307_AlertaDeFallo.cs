using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlertaDeFallo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerta",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "text", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    mensaje = table.Column<string>(type: "text", nullable: false),
                    ocurrido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    repeticiones = table.Column<int>(type: "integer", nullable: false)
                    // La columna xmin la genera EF pero no se crea: es una columna de sistema de
                    // PostgreSQL (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clave",
                table: "alerta",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_alerta_tipo_ocurrido_at",
                table: "alerta",
                columns: new[] { "tipo", "ocurrido_at" });

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): las tablas las crea rent_migrador,
            // así que cada migración nueva concede a rent_app sus permisos explícitamente.
            // alerta es de solo lectura para la aplicación, salvo que tiene que registrar los fallos:
            // por eso UPDATE (el contador de repeticiones). Nadie borra alertas.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON alerta TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerta");
        }
    }
}
