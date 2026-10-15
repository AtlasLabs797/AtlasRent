using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class ClientesYProspectos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cliente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    documento = table.Column<string>(type: "text", nullable: true),
                    documento_hmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    agente_retencion = table.Column<bool>(type: "boolean", nullable: false),
                    en_gestion_cobro = table.Column<bool>(type: "boolean", nullable: false),
                    recordatorios_pausados = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cliente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "prospecto",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    documento = table.Column<string>(type: "text", nullable: true),
                    documento_hmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    notas = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prospecto", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacto_cliente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    relacion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    es_principal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacto_cliente", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacto_cliente_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cliente_documento_hmac",
                table: "cliente",
                column: "documento_hmac",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cliente_nombre",
                table: "cliente",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_cliente_numero",
                table: "cliente",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacto_cliente_principal",
                table: "contacto_cliente",
                column: "cliente_id",
                unique: true,
                filter: "(es_principal = true)");

            migrationBuilder.CreateIndex(
                name: "ix_prospecto_documento_hmac",
                table: "prospecto",
                column: "documento_hmac",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prospecto_nombre",
                table: "prospecto",
                column: "nombre");

            // Número de cliente. La secuencia es de la base de datos y no un MAX(id)+1 en
            // código: eso repetiría números con dos altas a la vez (docs/DOC_TECNICA.md §4.3).
            // La importación de Factusol, que conserva números que ya existían, la deja por encima
            // del mayor importado al terminar.
            migrationBuilder.Sql(
                "CREATE SEQUENCE cliente_numero_seq AS integer START WITH 1 MINVALUE 1");

            // Permisos de la aplicación (docs/DOC_TECNICA.md §4.1). La secuencia necesita USAGE
            // para el nextval, SELECT para leer el contador y UPDATE para el setval que deja la
            // secuencia por encima del número importado.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE, DELETE
                    ON cliente, prospecto, contacto_cliente
                    TO rent_app;
                GRANT USAGE, SELECT, UPDATE ON SEQUENCE cliente_numero_seq TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contacto_cliente");

            migrationBuilder.DropTable(
                name: "prospecto");

            migrationBuilder.DropTable(
                name: "cliente");

            migrationBuilder.Sql("DROP SEQUENCE cliente_numero_seq");
        }
    }
}
