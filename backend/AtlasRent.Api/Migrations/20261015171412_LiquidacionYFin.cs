using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class LiquidacionYFin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F2-09: el reloj del cierre de una alerta. Nula = viva. Es la mitad que le faltaba a
            // `alerta` para poder ser persistente (§3.5): sin columna de cierre, "abierta hasta
            // liquidar" solo se podía resolver borrando la fila, y borrar el rastro de un fallo es
            // justo lo que AGENTS.md §6 prohíbe.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resuelta_at",
                table: "alerta",
                type: "timestamp with time zone",
                nullable: true);

            // F2-09: lo que se decidió al liquidar (fecha, quién, importe a cobrar por faltantes y
            // decisión sobre la fianza).
            //
            // `xmin` no se crea a propósito, igual que en las tablas de F2-08: es una columna de
            // sistema de PostgreSQL, ya existe en la tabla y crearla da error. El modelo la sigue
            // teniendo y el proveedor la usa sola en el WHERE de cada UPDATE.
            migrationBuilder.CreateTable(
                name: "contrato_liquidacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_inventario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_liquidacion = table.Column<DateOnly>(type: "date", nullable: false),
                    liquidado_por_id = table.Column<Guid>(type: "uuid", nullable: false),
                    importe_faltantes = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    decision_fianza = table.Column<int>(type: "integer", nullable: false),
                    fianza_contrato = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    fianza_retenida = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    observacion = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_liquidacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_contrato_liquidacion_contrato_contrato_id",
                        column: x => x.contrato_id,
                        principalTable: "contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contrato_liquidacion_revision_inventario_revision_inventari",
                        column: x => x.revision_inventario_id,
                        principalTable: "revision_inventario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_liquidacion_contrato_id",
                table: "contrato_liquidacion",
                column: "contrato_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contrato_liquidacion_revision_inventario_id",
                table: "contrato_liquidacion",
                column: "revision_inventario_id");

            // F2-09: la tabla la crea rent_migrador y 02_basedatos.sql solo declara permisos por
            // defecto para rent_owner (docs/DOC_TECNICA.md §4.3), así que sin esta línea la migración
            // aplica bien y la aplicación falla en runtime con "permission denied" al liquidar.
            //
            // UPDATE no entra y es a propósito: la liquidación nace cerrada y no se vuelve a escribir
            // (§3.5, y el 409 de re-liquidar que ya existía desde F2-06). DELETE tampoco: nada de esto
            // se borra (AGENTS.md §6). Si mañana hay que corregir una liquidación, se deshace el
            // estado del contrato por el camino que ya existe y se vuelve a liquidar; no se edita la
            // fila que dice lo que se decidió cerrar.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT ON contrato_liquidacion TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_liquidacion");

            migrationBuilder.DropColumn(
                name: "resuelta_at",
                table: "alerta");
        }
    }
}
