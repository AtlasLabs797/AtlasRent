using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class ComprobanteFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "referencia_efactura",
                table: "factura",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "comprobante_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    registrado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    anulado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    anulado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comprobante_fiscal", x => x.id);
                    table.CheckConstraint("ck_comprobante_fiscal_anulacion_completa", "estado = 1 OR (anulado_at IS NOT NULL AND anulado_por IS NOT NULL AND motivo_anulacion IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_comprobante_fiscal_factura_factura_id",
                        column: x => x.factura_id,
                        principalTable: "factura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_comprobante_fiscal_serie_serie_id",
                        column: x => x.serie_id,
                        principalTable: "serie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "numerador_fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_prefijo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    rango_desde = table.Column<int>(type: "integer", nullable: false),
                    rango_hasta = table.Column<int>(type: "integer", nullable: false),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    siguiente_numero = table.Column<int>(type: "integer", nullable: false),
                    umbral_alerta = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numerador_fiscal", x => x.id);
                    table.CheckConstraint("ck_numerador_fiscal_rango", "rango_desde <= rango_hasta");
                    table.CheckConstraint("ck_numerador_fiscal_siguiente_en_rango", "siguiente_numero >= rango_desde AND siguiente_numero <= rango_hasta + 1");
                    table.CheckConstraint("ck_numerador_fiscal_umbral", "umbral_alerta > 0");
                    table.ForeignKey(
                        name: "fk_numerador_fiscal_serie_serie_id",
                        column: x => x.serie_id,
                        principalTable: "serie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_fiscal_factura_id",
                table: "comprobante_fiscal",
                column: "factura_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_fiscal_serie_id_numero",
                table: "comprobante_fiscal",
                columns: new[] { "serie_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_numerador_fiscal_serie_id",
                table: "numerador_fiscal",
                column: "serie_id",
                unique: true);

            // F3-03: los permisos de las tablas nuevas van explícitos porque una migración nueva no
            // los trae: sin estas líneas la migración aplica bien y la aplicación falla en runtime con
            // «permission denied». La columna nueva de factura no necesita permiso aparte: el de la
            // tabla es el mismo y ya estaba concedido.
            //
            // numerador_fiscal sí lleva DELETE (a diferencia de comprobante_fiscal) porque es
            // configuración, no un documento: si el administrador crea un numerador por error y hay
            // que rehacerlo, se borra. Lo que nunca se borra es el comprobante ya emitido, que es
            // historia fiscal (D28, AGENTS.md §6), y por eso esa tabla no concede DELETE.
            //
            // Ninguna de las dos se siembra: el rango y el tipo de comprobante los autoriza alguien
            // fuera del sistema y ningún valor por defecto puede estar aquí (D14 y el criterio de
            // aceptación de docs/PLAN.md).
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE, DELETE ON numerador_fiscal TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON comprobante_fiscal TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "comprobante_fiscal");

            migrationBuilder.DropTable(
                name: "numerador_fiscal");

            migrationBuilder.DropColumn(
                name: "referencia_efactura",
                table: "factura");
        }
    }
}
