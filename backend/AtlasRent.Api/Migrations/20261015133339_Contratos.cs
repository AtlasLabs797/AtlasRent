using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Contratos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contrato",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    facturadora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    duracion_inicial_meses = table.Column<int>(type: "integer", nullable: false),
                    fecha_fin_inicial = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin_vigente = table.Column<DateOnly>(type: "date", nullable: false),
                    renta = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    dia_pago = table.Column<int>(type: "integer", nullable: false),
                    fianza = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    regla_renovacion = table.Column<int>(type: "integer", nullable: false),
                    renovacion_meses = table.Column<int>(type: "integer", nullable: false),
                    plantilla_contrato_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    plantilla_contrato_congelada_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    plantilla_congelada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    estado_actual_desde = table.Column<DateOnly>(type: "date", nullable: true),
                    estado_actual_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado_actual_comentario = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fecha_baja_programada = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_fin_real = table.Column<DateOnly>(type: "date", nullable: true),
                    observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin la genera EF pero no se crea: es una columna de sistema de
                    // PostgreSQL (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato", x => x.id);
                    table.ForeignKey(
                        name: "fk_contrato_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contrato_subempresa_facturadora_id",
                        column: x => x.facturadora_id,
                        principalTable: "subempresa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contrato_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contrato_version",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    cambio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    renta_anterior = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    renta_nueva = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    alcance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin la genera EF pero no se crea (aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_contrato_version_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "solicitud_aprobacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resultado = table.Column<int>(type: "integer", nullable: false),
                    huella_contenido = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    solicitado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    solicitado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelto_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    comentario = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    motivo_caducidad = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin la genera EF pero no se crea (aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_solicitud_aprobacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_solicitud_aprobacion_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_cliente_id",
                table: "contrato",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_contrato_estado_created_at",
                table: "contrato",
                columns: new[] { "estado", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_facturadora_id",
                table: "contrato",
                column: "facturadora_id");

            migrationBuilder.CreateIndex(
                name: "ix_contrato_unidad_id_estado",
                table: "contrato",
                columns: new[] { "unidad_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_version_contrato_id_numero",
                table: "contrato_version",
                columns: new[] { "contrato_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_aprobacion_contrato_id",
                table: "solicitud_aprobacion",
                column: "contrato_id",
                unique: true,
                filter: "(resultado = 1)");

            migrationBuilder.CreateIndex(
                name: "ix_solicitud_aprobacion_resultado_solicitado_at",
                table: "solicitud_aprobacion",
                columns: new[] { "resultado", "solicitado_at" });

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3, l.136-137): las tablas las crea
            // rent_migrador y 02_basedatos.sql solo declara permisos por defecto para rent_owner,
            // así que cada migración nueva concede los suyos a rent_app. Sin esta línea la migración
            // aplica bien y la aplicación falla en runtime con "permission denied", y ningún test lo
            // detecta porque los tests de integración no bajan a la aplicación con el rol rent_app.
            // DELETE no entra: un contrato, su historial y sus solicitudes no se borran nunca
            // (AGENTS.md §6), así que la aplicación no necesita ese permiso y antes que dejarlo
            // puesto está quitarlo.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON contrato, contrato_version, solicitud_aprobacion TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_version");

            migrationBuilder.DropTable(
                name: "solicitud_aprobacion");

            migrationBuilder.DropTable(
                name: "contrato");
        }
    }
}
