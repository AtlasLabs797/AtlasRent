using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenovacionesYIncrementos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contrato_bloque",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    renta = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_bloque", x => x.id);
                    table.ForeignKey(
                        name: "fk_contrato_bloque_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contrato_incremento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    porcentaje = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    fecha_vigencia = table.Column<DateOnly>(type: "date", nullable: false),
                    aplicado = table.Column<bool>(type: "boolean", nullable: false),
                    aplicado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    renta_antes = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    renta_despues = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_incremento", x => x.id);
                    table.CheckConstraint("ck_contrato_incremento_tipo_valor", "(tipo = 1 AND porcentaje IS NOT NULL AND importe IS NULL)\nOR (tipo = 2 AND importe IS NOT NULL AND porcentaje IS NULL)");
                    table.ForeignKey(
                        name: "fk_contrato_incremento_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_bloque_contrato_id_numero",
                table: "contrato_bloque",
                columns: new[] { "contrato_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contrato_incremento_contrato_id_aplicado_fecha_vigencia",
                table: "contrato_incremento",
                columns: new[] { "contrato_id", "aplicado", "fecha_vigencia" });

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): las tablas las crea rent_migrador y
            // 02_basedatos.sql solo declara permisos por defecto para rent_owner, así que cada
            // migración nueva concede los suyos a rent_app. Sin esta línea la migración aplica bien y
            // la aplicación falla en runtime con "permission denied", y ningún test lo detecta porque
            // los tests de integración no bajan a la aplicación con el rol rent_app.
            // UPDATE entra porque el trabajo de renovación abre el bloque, marca aplicado el
            // incremento y mueve la renta y el fin de bloque del contrato. DELETE no entra: ni un
            // bloque del historial ni un incremento con valor económico se borran nunca (AGENTS.md §6),
            // así que la aplicación no necesita ese permiso y antes que dejarlo puesto está quitarlo.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON contrato_bloque, contrato_incremento TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_bloque");

            migrationBuilder.DropTable(
                name: "contrato_incremento");
        }
    }
}
