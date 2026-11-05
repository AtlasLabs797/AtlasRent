using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Cobros : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cobro",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_efectiva = table.Column<DateOnly>(type: "date", nullable: false),
                    moneda_recibida = table.Column<int>(type: "integer", nullable: false),
                    importe_recibido = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    forma_pago_id = table.Column<Guid>(type: "uuid", nullable: false),
                    referencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    cuenta_receptora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_cambio_valor = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    tipo_cambio_fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo_cambio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    saldo_favor_usado = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    excedente = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_cobro", x => x.id);
                    table.CheckConstraint("ck_cobro_anulado_completo", "(estado = 2) = (anulado_at IS NOT NULL AND motivo_anulacion IS NOT NULL)");
                    table.CheckConstraint("ck_cobro_importe_positivo", "importe_recibido > 0");
                    table.CheckConstraint("ck_cobro_no_negativos", "saldo_favor_usado >= 0 AND excedente >= 0");
                    table.ForeignKey(
                        name: "fk_cobro_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobro_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobro_cuenta_receptora_cuenta_receptora_id",
                        column: x => x.cuenta_receptora_id,
                        principalTable: "cuenta_receptora",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobro_forma_pago_forma_pago_id",
                        column: x => x.forma_pago_id,
                        principalTable: "forma_pago",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cobro_tipo_cambio_tipo_cambio_id",
                        column: x => x.tipo_cambio_id,
                        principalTable: "tipo_cambio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "saldo_a_favor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saldo_a_favor", x => x.id);
                    table.CheckConstraint("ck_saldo_a_favor_no_negativo", "importe >= 0");
                    table.ForeignKey(
                        name: "fk_saldo_a_favor_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cobro_asignacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cobro_id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    vigente = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cobro_asignacion", x => x.id);
                    table.CheckConstraint("ck_cobro_asignacion_importe_positivo", "importe > 0");
                    table.ForeignKey(
                        name: "fk_cobro_asignacion_cobro_cobro_id",
                        column: x => x.cobro_id,
                        principalTable: "cobro",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cobro_asignacion_factura_factura_id",
                        column: x => x.factura_id,
                        principalTable: "factura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobro_cliente_id_fecha_efectiva",
                table: "cobro",
                columns: new[] { "cliente_id", "fecha_efectiva" });

            migrationBuilder.CreateIndex(
                name: "ix_cobro_contrato_id",
                table: "cobro",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobro_cuenta_receptora_id",
                table: "cobro",
                column: "cuenta_receptora_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobro_estado_fecha_efectiva",
                table: "cobro",
                columns: new[] { "estado", "fecha_efectiva" });

            migrationBuilder.CreateIndex(
                name: "ix_cobro_forma_pago_id",
                table: "cobro",
                column: "forma_pago_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobro_moneda_recibida_fecha_efectiva",
                table: "cobro",
                columns: new[] { "moneda_recibida", "fecha_efectiva" });

            migrationBuilder.CreateIndex(
                name: "ix_cobro_tipo_cambio_id",
                table: "cobro",
                column: "tipo_cambio_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobro_asignacion_cobro_id",
                table: "cobro_asignacion",
                column: "cobro_id");

            migrationBuilder.CreateIndex(
                name: "ix_cobro_asignacion_factura_id",
                table: "cobro_asignacion",
                column: "factura_id");

            migrationBuilder.CreateIndex(
                name: "ix_saldo_a_favor_contrato_id_moneda",
                table: "saldo_a_favor",
                columns: new[] { "contrato_id", "moneda" },
                unique: true);

            // F3-06: los permisos van explícitos porque una migración nueva no los trae: sin estas
            // líneas la migración aplica bien y la aplicación falla en runtime con "permission denied".
            //
            // Ninguna de las tres tablas lleva DELETE, y no es un descuido:
            //
            //  · cobro es un movimiento de dinero. Si se borra, el saldo del cliente se corrige
            //    mientras el banco sigue diciendo otra cosa, y no queda ni una fila que diga qué pasó.
            //    Un cobro se anula con motivo, que conserva el documento y explica la corrección
            //    (AGENTS.md §6, §3.10.1).
            //  · cobro_asignacion dice qué cobro pagó qué factura. Al anular un cobro la fila se
            //    queda sin vigor en vez de desaparecer; al anular la factura que pagaba pasa lo mismo
            //    (D6). Borrarla sería tirar el historial completo que §3.10.1 pide conservar.
            //  · saldo_a_favor es un saldo del contrato, no un documento. Cuando llega a cero la fila
            //    se queda con cero en vez de borrarse, para que se vea que ese crédito existió y se
            //    consumió.
            //
            // El borrado en cascada de cobro_asignacion sobre cobro no cambia esto: no hay DELETE
            // concedido a rent_app, así que solo un DROP de la tabla o un script de migración —
            // deliberado y con su propio motivo— puede llegar a usarlo.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON cobro TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON cobro_asignacion TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON saldo_a_favor TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los permisos se quiten antes de las tablas para que el reverso diga lo mismo que el
            // avance: DropTable los llevaría por delante sin que nadie lo viera.
            migrationBuilder.Sql(
                """
                REVOKE ALL ON saldo_a_favor FROM rent_app;
                REVOKE ALL ON cobro_asignacion FROM rent_app;
                REVOKE ALL ON cobro FROM rent_app;
                """);

            migrationBuilder.DropTable(
                name: "cobro_asignacion");

            migrationBuilder.DropTable(
                name: "saldo_a_favor");

            migrationBuilder.DropTable(
                name: "cobro");
        }
    }
}
