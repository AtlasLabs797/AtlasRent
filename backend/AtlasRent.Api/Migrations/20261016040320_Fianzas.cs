using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Fianzas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                table: "cobro_asignacion",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "fianza_id",
                table: "cobro_asignacion",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fianza",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe_exigido = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    numero_fianzas = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    retenida_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    en_liquidacion_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    devuelta_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fianza", x => x.id);
                    table.CheckConstraint("ck_fianza_importe_positivo", "importe_exigido > 0");
                    table.CheckConstraint("ck_fianza_numero_positivo", "numero_fianzas > 0");
                    table.ForeignKey(
                        name: "fk_fianza_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fianza_devolucion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fianza_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    importe_descontado = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fianza_devolucion", x => x.id);
                    table.CheckConstraint("ck_fianza_devolucion_descontado_con_motivo", "importe_descontado = 0 OR motivo IS NOT NULL");
                    table.CheckConstraint("ck_fianza_devolucion_importe_al_menos_uno", "importe > 0 OR importe_descontado > 0");
                    table.CheckConstraint("ck_fianza_devolucion_importe_no_negativo", "importe >= 0 AND importe_descontado >= 0");
                    table.ForeignKey(
                        name: "fk_fianza_devolucion_fianza_fianza_id",
                        column: x => x.fianza_id,
                        principalTable: "fianza",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cobro_asignacion_fianza_id",
                table: "cobro_asignacion",
                column: "fianza_id");

            migrationBuilder.CreateIndex(
                name: "ux_cobro_asignacion_cobro_factura",
                table: "cobro_asignacion",
                columns: new[] { "cobro_id", "factura_id" },
                unique: true,
                filter: "factura_id IS NOT NULL AND vigente");

            migrationBuilder.CreateIndex(
                name: "ux_cobro_asignacion_cobro_fianza",
                table: "cobro_asignacion",
                columns: new[] { "cobro_id", "fianza_id" },
                unique: true,
                filter: "fianza_id IS NOT NULL AND vigente");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cobro_asignacion_destino_unico",
                table: "cobro_asignacion",
                sql: "(factura_id IS NULL) <> (fianza_id IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_fianza_contrato_id",
                table: "fianza",
                column: "contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_fianza_contrato_moneda",
                table: "fianza",
                columns: new[] { "contrato_id", "moneda" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fianza_devolucion_fianza_id",
                table: "fianza_devolucion",
                column: "fianza_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cobro_asignacion_fianza_fianza_id",
                table: "cobro_asignacion",
                column: "fianza_id",
                principalTable: "fianza",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Toda migración nueva concede sus permisos a rent_app explícitamente (docs/DOC_TECNICA.md
            // §4.3): 02_basedatos.sql solo declara los permisos por defecto FOR ROLE rent_owner, y las
            // tablas las crea rent_migrador.
            //
            // Sin DELETE, como todas las de F1 en adelante: nada se borra nunca (AGENTS.md §6). Una
            // devolución mal hecha se corrige con otra con su motivo, y la fila equivocada se queda
            // porque quedarse es lo que permite entender lo pasó.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON fianza TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON fianza_devolucion TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cobro_asignacion_fianza_fianza_id",
                table: "cobro_asignacion");

            migrationBuilder.DropTable(
                name: "fianza_devolucion");

            migrationBuilder.DropTable(
                name: "fianza");

            migrationBuilder.DropIndex(
                name: "ix_cobro_asignacion_fianza_id",
                table: "cobro_asignacion");

            migrationBuilder.DropIndex(
                name: "ux_cobro_asignacion_cobro_factura",
                table: "cobro_asignacion");

            migrationBuilder.DropIndex(
                name: "ux_cobro_asignacion_cobro_fianza",
                table: "cobro_asignacion");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cobro_asignacion_destino_unico",
                table: "cobro_asignacion");

            migrationBuilder.DropColumn(
                name: "fianza_id",
                table: "cobro_asignacion");

            migrationBuilder.AlterColumn<Guid>(
                name: "factura_id",
                table: "cobro_asignacion",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
