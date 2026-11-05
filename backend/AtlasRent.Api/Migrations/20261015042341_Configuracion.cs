using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class Configuracion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ajuste_general",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tolerancia_cambio_usd = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    tolerancia_cambio_dop = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    modo_recordatorios = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ajuste_general", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cuenta_receptora",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    banco = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuenta_receptora", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "forma_pago",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forma_pago", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "idempotencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ruta = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    huella = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    respuesta = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotencia", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "numero_serie",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    serie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    emitido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emitido_por = table.Column<Guid>(type: "uuid", nullable: false),
                    anulado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    anulado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_anulacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numero_serie", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "serie",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    prefijo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    moneda = table.Column<int>(type: "integer", nullable: true),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    plantilla_pdf = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    suma_a_facturado = table.Column<bool>(type: "boolean", nullable: false),
                    suma_a_saldos = table.Column<bool>(type: "boolean", nullable: false),
                    genera_comprobante = table.Column<bool>(type: "boolean", nullable: false),
                    siguiente = table.Column<int>(type: "integer", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serie", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "subempresa",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    propietaria = table.Column<bool>(type: "boolean", nullable: false),
                    facturadora = table.Column<bool>(type: "boolean", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subempresa", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tipo_itbis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    porcentaje = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipo_itbis", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tipo_retencion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    porcentaje = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    @base = table.Column<int>(name: "base", type: "integer", nullable: false),
                    es_defecto = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipo_retencion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuenta_receptora_nombre",
                table: "cuenta_receptora",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_forma_pago_nombre",
                table: "forma_pago",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_idempotencia_usuario_id_clave",
                table: "idempotencia",
                columns: new[] { "usuario_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_numero_serie_serie_id",
                table: "numero_serie",
                column: "serie_id");

            migrationBuilder.CreateIndex(
                name: "ix_numero_serie_serie_id_numero",
                table: "numero_serie",
                columns: new[] { "serie_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serie_numero",
                table: "serie",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serie_prefijo",
                table: "serie",
                column: "prefijo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subempresa_nombre",
                table: "subempresa",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_tipo_itbis_nombre",
                table: "tipo_itbis",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_tipo_retencion_es_defecto",
                table: "tipo_retencion",
                column: "es_defecto",
                unique: true,
                filter: "(es_defecto = true)");

            migrationBuilder.CreateIndex(
                name: "ix_tipo_retencion_nombre",
                table: "tipo_retencion",
                column: "nombre");

            // Series iniciales de docs/ESPECIFICACION.md §3.7: 1 USD, 2 DOP, 3 fiscal de moneda
            // libre, 4 proforma de moneda libre y 9 histórica solo para importación.
            // El prefijo queda nulo porque la especificación da los números, no los prefijos, y el
            // contador arranca en 1 porque todavía no se ha emitido nada (TODO en Serie.Prefijo).
            //
            // TODO(A#): ¿la serie histórica (9) debe sumar a facturado y a saldos? Las facturas
            // importadas de Factusol ya están en los informes antiguos; aquí sale que no, y que lo
            // confirme el responsable.
            migrationBuilder.Sql(
                """
                INSERT INTO serie
                    (id, numero, prefijo, moneda, tipo, suma_a_facturado, suma_a_saldos, genera_comprobante, siguiente, activa, created_at)
                VALUES
                    (gen_random_uuid(), 1, NULL, 1, 1, true, true, false, 1, true, now()),
                    (gen_random_uuid(), 2, NULL, 2, 1, true, true, false, 1, true, now()),
                    (gen_random_uuid(), 3, NULL, NULL, 3, true, true, true, 1, true, now()),
                    (gen_random_uuid(), 4, NULL, NULL, 2, false, false, false, 1, true, now()),
                    (gen_random_uuid(), 9, NULL, NULL, 4, false, false, false, 1, true, now());
                """);

            // La fila única de ajustes generales. Las tolerancias nacen nulas y los recordatorios
            // apagados: la especificación da los importes de la tolerancia como supuesto (TODO en
            // AjusteGeneral) y nadie envía mensajes sin que el administrador lo pida (D12).
            migrationBuilder.Sql(
                """
                INSERT INTO ajuste_general (id, modo_recordatorios, created_at)
                VALUES (gen_random_uuid(), 1, now());
                """);

            // Permisos de la aplicación (docs/DOC_TECNICA.md §4.1): la configuración se lee y se
            // escribe con la API, y numero_serie lo escribe el contador de la emisión.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE, DELETE
                    ON serie, numero_serie, tipo_itbis, tipo_retencion, forma_pago,
                       cuenta_receptora, subempresa, ajuste_general, idempotencia
                    TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ajuste_general");

            migrationBuilder.DropTable(
                name: "cuenta_receptora");

            migrationBuilder.DropTable(
                name: "forma_pago");

            migrationBuilder.DropTable(
                name: "idempotencia");

            migrationBuilder.DropTable(
                name: "numero_serie");

            migrationBuilder.DropTable(
                name: "serie");

            migrationBuilder.DropTable(
                name: "subempresa");

            migrationBuilder.DropTable(
                name: "tipo_itbis");

            migrationBuilder.DropTable(
                name: "tipo_retencion");
        }
    }
}
