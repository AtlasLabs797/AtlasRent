using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AutenticacionYUsuarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_acceso = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    debe_cambiar_password = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_activado = table.Column<bool>(type: "boolean", nullable: false),
                    ultimo_acceso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                    // La columna xmin la genera EF pero no se crea: es una columna de sistema de
                    // PostgreSQL (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sesion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    familia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: false),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    emitida_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultima_actividad = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    caduca_absoluta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_revocacion = table.Column<string>(type: "text", nullable: true)
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sesion", x => x.id);
                    table.ForeignKey(
                        name: "fk_sesion_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sesion_familia_id",
                table: "sesion",
                column: "familia_id");

            migrationBuilder.CreateIndex(
                name: "ix_sesion_token_hash",
                table: "sesion",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sesion_usuario_id",
                table: "sesion",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_nombre_acceso",
                table: "usuario",
                column: "nombre_acceso",
                unique: true);

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): 02_basedatos.sql declara los
            // permisos por defecto FOR ROLE rent_owner, pero las tablas las crea rent_migrador.
            // Toda migración nueva tiene que conceder sus permisos a rent_app explícitamente.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE, DELETE ON usuario TO rent_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON sesion TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sesion");

            migrationBuilder.DropTable(
                name: "usuario");
        }
    }
}
