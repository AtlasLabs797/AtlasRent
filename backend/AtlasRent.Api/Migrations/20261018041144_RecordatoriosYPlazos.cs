using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecordatoriosYPlazos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F4-05: los plazos del aviso de pago (§3.14, D12) viven en `ajuste_general` y no en el
            // código (AGENTS.md §6), así que los siembra la migración: el primero a los 5 días del
            // vencimiento y la repetición cada 15, que es el supuesto que escribe la especificación.
            //
            // Anulables a propósito. Con los valores de fábrica, ponerlos a nula es una decisión
            // consciente («de momento no avisa a nadie») y el trabajo de recordatorios lo respeta
            // levantando una alerta que diga qué configurar (criterio de BUG-007). El DEFAULT solo
            // rellena las filas que ya existían y las que se creen sin decir nada; no impide que se
            // guarden a nula.
            migrationBuilder.AddColumn<int>(
                name: "dias_primer_recordatorio",
                table: "ajuste_general",
                type: "integer",
                nullable: true,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "dias_entre_recordatorios",
                table: "ajuste_general",
                type: "integer",
                nullable: true,
                defaultValue: 15);

            migrationBuilder.CreateTable(
                name: "recordatorio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    envio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_omision = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recordatorio", x => x.id);
                    table.ForeignKey(
                        name: "fk_recordatorio_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recordatorio_envio_envio_id",
                        column: x => x.envio_id,
                        principalTable: "envio",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_recordatorio_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recordatorio_cliente",
                table: "recordatorio",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_recordatorio_estado",
                table: "recordatorio",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_recordatorio_fecha",
                table: "recordatorio",
                column: "fecha");

            // El único de (cliente, unidad, fecha) es lo que hace que correr el trabajo dos veces el
            // mismo día no mande dos avisos (§8.0). Se escribe a mano y no con CreateIndex porque
            // `unidad_id` puede ser nula y en PostgreSQL los nulos de un único no chocan entre sí:
            // sin NULLS NOT DISTINCT, un cliente sin contrato con unidad recibiría un aviso por día por
            // cada intento. Es el mismo motivo y el mismo trato que ix_factura_periodo_unico.
            //
            // El modelo de EF declara este mismo índice para que lo conozca, y por eso una migración
            // posterior que lo volviera a crear perdería el NULLS NOT DISTINCT: si algún día cambia,
            // hay que volver a escribirlo a mano.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_recordatorio_cliente_unidad_fecha
                    ON recordatorio (cliente_id, unidad_id, fecha)
                    NULLS NOT DISTINCT;
                """);

            // La plantilla de recordatorio que sembró F4-02 hablaba de «la factura {numero}». Un
            // recordatorio no es una factura: §3.14 lo quiere «por cliente y unidad», con texto corto
            // que menciona solo el pendiente y con el estado de cuenta adjunto. Se corrige aquí y no
            // en el código porque el texto es configuración, y el filtro es el de la fila sembrada
            // (tipo 3 en español).
            migrationBuilder.Sql(
                """
                UPDATE plantilla_mensaje
                SET asunto = 'Recordatorio de pago de {cliente}',
                    cuerpo = E'Estimado {cliente}:\r\n\r\nLe recordamos que tiene pendiente {pendiente}, con vencimiento el {vencimiento}.\r\n{unidad}\r\nLe adjuntamos su estado de cuenta con el detalle.\r\n\r\nSaludos,\r\nAtlas Rent',
                    updated_at = now()
                WHERE tipo = 3 AND idioma = 'es';
                """);

            // F4-05. Sin DELETE, como todas las de F1 en adelante: nada se borra nunca (AGENTS.md §6).
            // Un recordatorio es la explicación de por qué a un cliente se le escribió o no se le
            // escribió un día dado, y esa explicación no se puede perder.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON recordatorio TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recordatorio");

            migrationBuilder.DropColumn(
                name: "dias_entre_recordatorios",
                table: "ajuste_general");

            migrationBuilder.DropColumn(
                name: "dias_primer_recordatorio",
                table: "ajuste_general");
        }
    }
}