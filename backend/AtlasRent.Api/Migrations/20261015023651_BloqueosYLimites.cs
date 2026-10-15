using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class BloqueosYLimites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "bloqueada_hasta",
                table: "usuario",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bloqueo_ip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: false),
                    nivel = table.Column<int>(type: "integer", nullable: false),
                    fallos = table.Column<int>(type: "integer", nullable: false),
                    bloqueada_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    // La columna xmin la genera EF pero no se crea: es una columna de sistema de
                    // PostgreSQL (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bloqueo_ip", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "intento_login",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_acceso = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: false),
                    exito = table.Column<bool>(type: "boolean", nullable: false),
                    ocurrido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intento_login", x => x.id);
                    table.ForeignKey(
                        name: "fk_intento_login_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_bloqueo_ip_ip",
                table: "bloqueo_ip",
                column: "ip",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_intento_login_ip_ocurrido_at",
                table: "intento_login",
                columns: new[] { "ip", "ocurrido_at" });

            migrationBuilder.CreateIndex(
                name: "ix_intento_login_usuario_id_exito",
                table: "intento_login",
                columns: new[] { "usuario_id", "exito" });

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): 02_basedatos.sql declara los
            // permisos por defecto FOR ROLE rent_owner, pero las tablas las crea rent_migrador.
            // Toda migración nueva tiene que conceder sus permisos a rent_app explícitamente.
            // intento_login es un registro de seguridad: la API solo lo inserta y lo lee, no lo
            // actualiza ni lo borra (el borrado es del job de limpieza, con rent_migrador).
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT ON intento_login TO rent_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON bloqueo_ip TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bloqueo_ip");

            migrationBuilder.DropTable(
                name: "intento_login");

            migrationBuilder.DropColumn(
                name: "bloqueada_hasta",
                table: "usuario");
        }
    }
}
