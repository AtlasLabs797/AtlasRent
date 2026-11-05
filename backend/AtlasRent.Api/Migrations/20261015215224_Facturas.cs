using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Facturas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "factura",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    serie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    fecha_emision = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    concepto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    itbis = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tipo_cambio_valor = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    tipo_cambio_fecha = table.Column<DateOnly>(type: "date", nullable: true),
                    tipo_cambio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factura", x => x.id);
                    table.CheckConstraint("ck_factura_cambio_con_emision", "tipo_cambio_fecha IS NULL OR fecha_emision IS NOT NULL");
                    table.CheckConstraint("ck_factura_numero_estado", "(estado = 1) = (numero IS NULL)");
                    table.ForeignKey(
                        name: "fk_factura_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_factura_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_factura_serie_serie_id",
                        column: x => x.serie_id,
                        principalTable: "serie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_factura_tipo_cambio_tipo_cambio_id",
                        column: x => x.tipo_cambio_id,
                        principalTable: "tipo_cambio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_factura_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "factura_linea",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posicion = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    concepto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cantidad = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    precio_unitario = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    tipo_itbis_id = table.Column<Guid>(type: "uuid", nullable: true),
                    porcentaje_itbis = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    itbis_importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_factura_linea", x => x.id);
                    table.ForeignKey(
                        name: "fk_factura_linea_factura_factura_id",
                        column: x => x.factura_id,
                        principalTable: "factura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_factura_linea_tipo_itbis_tipo_itbis_id",
                        column: x => x.tipo_itbis_id,
                        principalTable: "tipo_itbis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_factura_cliente_id",
                table: "factura",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_factura_contrato_id",
                table: "factura",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_factura_estado_fecha_emision",
                table: "factura",
                columns: new[] { "estado", "fecha_emision" });

            migrationBuilder.CreateIndex(
                name: "ix_factura_serie_id_numero",
                table: "factura",
                columns: new[] { "serie_id", "numero" },
                unique: true,
                filter: "numero IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_factura_tipo_cambio_id",
                table: "factura",
                column: "tipo_cambio_id");

            migrationBuilder.CreateIndex(
                name: "ix_factura_unidad_id",
                table: "factura",
                column: "unidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_factura_linea_factura_id_posicion",
                table: "factura_linea",
                columns: new[] { "factura_id", "posicion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_factura_linea_tipo_itbis_id",
                table: "factura_linea",
                column: "tipo_itbis_id");

            // La serie 9 (histórica) queda desactivada. No emite: la usan las importaciones de
            // Factusol conservando el número que ya tenían (Fase M), y un contador que arrancara en 1
            // y se moviera no significaría nada ahí. F1-08 la sembró activa porque aún no había
            // facturas; esta es la primera migración que ya sabe para qué sirve.
            migrationBuilder.Sql(
                """
                UPDATE serie SET activa = false WHERE numero = 9;
                """);

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): las tablas las crea rent_migrador y
            // 02_basedatos.sql solo declara permisos por defecto para rent_owner, así que cada
            // migración nueva concede los suyos a rent_app. Sin estas líneas la migración aplica bien
            // y la aplicación falla en runtime con "permission denied".
            // factura no lleva DELETE porque una factura tiene efecto económico y no se borra nunca
            // (AGENTS.md §6): lo que se hace con la emitida es anularla (F3-08). factura_linea sí lo
            // lleva, porque las líneas de un borrador se dan de alta, se editan y se borran mientras
            // el documento no tiene número, ni comprobante, ni deuda.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON factura TO rent_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON factura_linea TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los permisos se quitan antes de las tablas para que el reverso diga lo mismo que el
            // avance: DropTable los llevaría por delante sin que nadie lo viera.
            migrationBuilder.Sql(
                """
                REVOKE ALL ON factura_linea FROM rent_app;
                REVOKE ALL ON factura FROM rent_app;
                """);

            // La serie 9 vuelve a estar como la dejó F1-08. Su estado activo no fue un dato de F3-02,
            // fue un valor provisional de cuando no había facturas que emitir.
            migrationBuilder.Sql(
                """
                UPDATE serie SET activa = true WHERE numero = 9;
                """);

            migrationBuilder.DropTable(
                name: "factura_linea");

            migrationBuilder.DropTable(
                name: "factura");
        }
    }
}
