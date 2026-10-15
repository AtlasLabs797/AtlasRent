using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Cotizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cotizacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prospecto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin la genera EF pero no se crea: es una columna de sistema de
                    // PostgreSQL (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizacion", x => x.id);
                    table.CheckConstraint("ck_cotizacion_destinatario", "(cliente_id IS NOT NULL) <> (prospecto_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_cotizacion_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cotizacion_prospecto_prospecto_id",
                        column: x => x.prospecto_id,
                        principalTable: "prospecto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cotizacion_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cotizacion_linea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cotizacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin la genera EF pero no se crea (aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cotizacion_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_cotizacion_linea_cotizacion_cotizacion_id",
                        column: x => x.cotizacion_id,
                        principalTable: "cotizacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_activo_estado",
                table: "cotizacion",
                columns: new[] { "activo", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_cliente_id",
                table: "cotizacion",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_created_at",
                table: "cotizacion",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_prospecto_id",
                table: "cotizacion",
                column: "prospecto_id");

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_unidad_id",
                table: "cotizacion",
                column: "unidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_cotizacion_linea_cotizacion_id_posicion",
                table: "cotizacion_linea",
                columns: new[] { "cotizacion_id", "posicion" },
                unique: true);

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): las tablas las crea
            // rent_migrador y 02_basedatos.sql solo declara permisos por defecto para rent_owner,
            // así que cada migración nueva concede los suyos a rent_app. Sin esta línea la
            // migración aplica bien y la aplicación falla en runtime con "permission denied".
            // cotizacion no lleva DELETE porque nunca se borra una fila con efecto económico: la
            // baja es lógica (activo = false). cotizacion_linea sí lo lleva, porque las líneas de
            // un borrador se reemplazan enteras al editarlo; una vez enviada la cotización, la
            // aplicación no vuelve a tocar sus líneas.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON cotizacion TO rent_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON cotizacion_linea TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cotizacion_linea");

            migrationBuilder.DropTable(
                name: "cotizacion");
        }
    }
}
