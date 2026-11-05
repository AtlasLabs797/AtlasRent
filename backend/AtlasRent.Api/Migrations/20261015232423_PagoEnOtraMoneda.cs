using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class PagoEnOtraMoneda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "importe_recibido",
                table: "cobro_asignacion",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "tipo_cambio_valor",
                table: "cobro_asignacion",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ajuste_diferencia_cambio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    tipo_cambio_valor = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ip = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_ajuste_diferencia_cambio", x => x.id);
                    table.CheckConstraint("ck_ajuste_diferencia_cambio_anulacion_completa", "(anulado_at IS NULL) = (anulado_por IS NULL AND motivo_anulacion IS NULL)");
                    table.CheckConstraint("ck_ajuste_diferencia_cambio_motivo", "length(trim(motivo)) > 0");
                    table.CheckConstraint("ck_ajuste_diferencia_cambio_positivo", "importe > 0");
                    table.CheckConstraint("ck_ajuste_diferencia_cambio_tipo_cambio", "tipo_cambio_valor > 0");
                    table.ForeignKey(
                        name: "fk_ajuste_diferencia_cambio_factura_factura_id",
                        column: x => x.factura_id,
                        principalTable: "factura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_cobro_asignacion_conversion_completa",
                table: "cobro_asignacion",
                sql: "(importe_recibido IS NULL) = (tipo_cambio_valor IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cobro_asignacion_conversion_positiva",
                table: "cobro_asignacion",
                sql: "(importe_recibido IS NULL OR importe_recibido > 0) AND (tipo_cambio_valor IS NULL OR tipo_cambio_valor > 0)");

            migrationBuilder.CreateIndex(
                name: "ix_ajuste_diferencia_cambio_factura_id",
                table: "ajuste_diferencia_cambio",
                column: "factura_id");

            // F3-07: los permisos de la tabla nueva van explícitos porque una migración nueva no los
            // trae: sin estas líneas la migración aplica bien y la aplicación falla en runtime con
            // «permission denied». Las columnas nuevas de cobro_asignacion no necesitan permiso aparte:
            // el de la tabla es el mismo y ya estaba concedido.
            //
            // ajuste_diferencia_cambio no lleva DELETE, y no es un descuido: corrige un saldo de
            // factura, igual que un cobro es un movimiento de dinero. Si se borra, el pendiente
            // vuelve a aparecer sin que quede ni una fila que diga por qué se había cerrado. Un
            // ajuste equivocado se anula con motivo, que conserva el documento y explica la corrección
            // (AGENTS.md §6).
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON ajuste_diferencia_cambio TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ajuste_diferencia_cambio");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cobro_asignacion_conversion_completa",
                table: "cobro_asignacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cobro_asignacion_conversion_positiva",
                table: "cobro_asignacion");

            migrationBuilder.DropColumn(
                name: "importe_recibido",
                table: "cobro_asignacion");

            migrationBuilder.DropColumn(
                name: "tipo_cambio_valor",
                table: "cobro_asignacion");
        }
    }
}
