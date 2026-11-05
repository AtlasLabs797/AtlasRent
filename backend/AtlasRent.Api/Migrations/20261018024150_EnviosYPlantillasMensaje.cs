using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnviosYPlantillasMensaje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "envio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_documento = table.Column<int>(type: "integer", nullable: false),
                    documento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento_anio = table.Column<int>(type: "integer", nullable: true),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contacto_cliente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destinatario_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    destinatario_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    asunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    idioma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    pdf_copia = table.Column<byte[]>(type: "bytea", nullable: false),
                    nombre_archivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    documento_modificado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    enviado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entregado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    leido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_envio", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plantilla_mensaje",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    idioma = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    asunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantilla_mensaje", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_envio_documento_destinatario",
                table: "envio",
                columns: new[] { "tipo_documento", "documento_id", "documento_anio", "destinatario_email" });

            migrationBuilder.CreateIndex(
                name: "ix_envio_estado",
                table: "envio",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_envio_tipo_documento",
                table: "envio",
                columns: new[] { "tipo_documento", "documento_id", "documento_anio" });

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_mensaje_tipo_idioma",
                table: "plantilla_mensaje",
                columns: new[] { "tipo", "idioma" },
                unique: true);

            // F4-02: las cinco plantillas de mensaje en español (docs/ESPECIFICACION.md §3.14).
            //
            // Se siembran porque sin plantilla no hay envío, y un envío que falla porque a nadie se le
            // ocurrió escribir un texto no es un envío que se pueda arreglar luego: el cliente ya
            // estaba esperando el correo. El idioma es el único que se siembra; los demás los crea
            // quien administre, y si se pide uno que no existe el envío sale con el español y la fila
            // guarda el idioma real (ServicioEnvios.PlantillaAsync).
            //
            // Los identificadores son fijos y no secuenciales: son valores de siembra, no datos de una
            // entidad que alguien pueda buscar, y fijarlos hace que la migración sea idempotente
            // aunque se aplique dos veces sobre una base ya sembrada.
            migrationBuilder.InsertData(
                table: "plantilla_mensaje",
                columns: new[] { "id", "tipo", "idioma", "asunto", "cuerpo", "activa", "created_at" },
                values: new object[,]
                {
                    // Cotización
                    {
                        new Guid("201a21ee-8497-4a20-8d49-2905d917d214"), 1, "es",
                        "Cotización de {unidad}",
                        "Estimado {cliente}:\r\n\r\nLe adjuntamos la cotización de {unidad}.\r\n\r\n"
                        + "Saludos,\r\nAtlas Rent",
                        true, new DateTimeOffset(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)),
                    },

                    // Factura o proforma
                    {
                        new Guid("0a699e0b-6c1a-4f5e-94d7-387cbc15a040"), 2, "es",
                        "Factura {numero} de {unidad}",
                        "Estimado {cliente}:\r\n\r\nLe adjuntamos la factura {numero} por un total de {total}, "
                        + "con vencimiento el {vencimiento}.\r\n\r\nSaludos,\r\nAtlas Rent",
                        true, new DateTimeOffset(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)),
                    },

                    // Recordatorio de vencimiento (W5)
                    {
                        new Guid("758a6ccc-946f-4a7c-8145-567ace3264bb"), 3, "es",
                        "Recordatorio de la factura {numero}",
                        "Estimado {cliente}:\r\n\r\nLe recordamos que la factura {numero} venció el "
                        + "{vencimiento} y que queda un pendiente de {pendiente}.\r\n\r\nSaludos,\r\nAtlas Rent",
                        true, new DateTimeOffset(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)),
                    },

                    // Recibo de cobro
                    {
                        new Guid("c000899c-d7ed-4c0d-a7cf-83137a45009b"), 4, "es",
                        "Recibo {numero}",
                        "Estimado {cliente}:\r\n\r\nLe adjuntamos el recibo {numero} por {total}.\r\n\r\n"
                        + "Saludos,\r\nAtlas Rent",
                        true, new DateTimeOffset(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)),
                    },

                    // Estado de cuenta
                    {
                        new Guid("232bd92c-232e-4116-9ff9-9cb33bbf0d01"), 5, "es",
                        "Estado de cuenta {numero}",
                        "Estimado {cliente}:\r\n\r\nLe adjuntamos su estado de cuenta {numero}. "
                        + "El pendiente total es {pendiente}.\r\n\r\nSaludos,\r\nAtlas Rent",
                        true, new DateTimeOffset(new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)),
                    },
                });

            // F4-02. Sin DELETE, como todas las de F1 en adelante: nada se borra nunca (AGENTS.md §6).
            // Un envío es la prueba de lo que se le mandó al cliente; borrarlo sería borrar el
            // comprobante de lo que el cliente recibió. Una plantilla se desactiva, no se borra.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON envio TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON plantilla_mensaje TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "envio");

            migrationBuilder.DropTable(
                name: "plantilla_mensaje");
        }
    }
}
