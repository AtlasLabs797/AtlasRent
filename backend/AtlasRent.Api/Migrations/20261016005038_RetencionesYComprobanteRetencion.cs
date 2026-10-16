using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class RetencionesYComprobanteRetencion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "base_retencion",
                table: "factura_linea",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "porcentaje_retencion",
                table: "factura_linea",
                type: "numeric(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tipo_retencion_id",
                table: "factura_linea",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "neto_a_cobrar",
                table: "factura",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "retencion",
                table: "factura",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            // Las columnas nuevas llegan a cero, pero el neto a cobrar de una factura ya emitida no
            // era cero: era su total. Esta vuelta va ANTES de los constraints de abajo porque al
            // añadir uno PostgreSQL valida las filas que ya están, y con la tabla a medias la
            // migración no aplicaría sobre una base con datos — que es justo donde no puede fallar.
            migrationBuilder.Sql(
                """
                UPDATE factura SET neto_a_cobrar = total - retencion;
                """);

            migrationBuilder.CreateTable(
                name: "comprobante_retencion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    factura_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    @base = table.Column<int>(name: "base", type: "integer", nullable: false),
                    porcentaje = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    numero = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    fecha_recepcion = table.Column<DateOnly>(type: "date", nullable: true),
                    recibido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    recibido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comprobante_retencion", x => x.id);
                    table.CheckConstraint("ck_comprobante_retencion_importe", "importe > 0");
                    table.CheckConstraint("ck_comprobante_retencion_porcentaje", "porcentaje >= 0");
                    table.CheckConstraint("ck_comprobante_retencion_recibido_completo", "(estado = 1) = (numero IS NULL AND fecha_recepcion IS NULL AND recibido_at IS NULL AND recibido_por IS NULL)");
                    table.ForeignKey(
                        name: "fk_comprobante_retencion_factura_factura_id",
                        column: x => x.factura_id,
                        principalTable: "factura",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_factura_linea_tipo_retencion_id",
                table: "factura_linea",
                column: "tipo_retencion_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_factura_neto_a_cobrar",
                table: "factura",
                sql: "neto_a_cobrar = total - retencion");

            migrationBuilder.AddCheckConstraint(
                name: "ck_factura_retencion_no_negativa",
                table: "factura",
                sql: "retencion >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_retencion_estado",
                table: "comprobante_retencion",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_comprobante_retencion_factura_id",
                table: "comprobante_retencion",
                column: "factura_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_factura_linea_tipo_retencion_tipo_retencion_id",
                table: "factura_linea",
                column: "tipo_retencion_id",
                principalTable: "tipo_retencion",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Sin DELETE, igual que comprobante_fiscal: una retención emitida y su comprobante son
            // un hecho económico. Si algo está mal, lo que hay es anular la factura (F3-08), no
            // borrar la fila que dice que se retuvo.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON comprobante_retencion TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_factura_linea_tipo_retencion_tipo_retencion_id",
                table: "factura_linea");

            migrationBuilder.DropTable(
                name: "comprobante_retencion");

            migrationBuilder.DropIndex(
                name: "ix_factura_linea_tipo_retencion_id",
                table: "factura_linea");

            migrationBuilder.DropCheckConstraint(
                name: "ck_factura_neto_a_cobrar",
                table: "factura");

            migrationBuilder.DropCheckConstraint(
                name: "ck_factura_retencion_no_negativa",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "base_retencion",
                table: "factura_linea");

            migrationBuilder.DropColumn(
                name: "porcentaje_retencion",
                table: "factura_linea");

            migrationBuilder.DropColumn(
                name: "tipo_retencion_id",
                table: "factura_linea");

            migrationBuilder.DropColumn(
                name: "neto_a_cobrar",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "retencion",
                table: "factura");
        }
    }
}
