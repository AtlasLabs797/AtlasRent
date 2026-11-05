using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class MfaYAccesoEmergencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_admin",
                table: "usuario",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mfa_secreto",
                table: "usuario",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "modo_emergencia",
                table: "sesion",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "acceso_emergencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitante_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aprobado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "text", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: false),
                    codigo_hash = table.Column<string>(type: "text", nullable: true),
                    creado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aprobado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    caduca_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_acceso_emergencia", x => x.id);
                    table.ForeignKey(
                        name: "fk_acceso_emergencia_usuario_aprobado_por_id",
                        column: x => x.aprobado_por_id,
                        principalTable: "usuario",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_acceso_emergencia_usuario_solicitante_id",
                        column: x => x.solicitante_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_acceso_emergencia_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "codigo_recuperacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_hash = table.Column<string>(type: "text", nullable: false),
                    creado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_codigo_recuperacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_codigo_recuperacion_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mfa_desafio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    ip = table.Column<string>(type: "text", nullable: false),
                    emitido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    caduca_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                    // xmin: columna de sistema de PostgreSQL, no se crea (docs/DOC_TECNICA.md §4.3).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_desafio", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_desafio_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_acceso_emergencia_aprobado_por_id",
                table: "acceso_emergencia",
                column: "aprobado_por_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceso_emergencia_solicitante_id",
                table: "acceso_emergencia",
                column: "solicitante_id");

            migrationBuilder.CreateIndex(
                name: "ix_acceso_emergencia_usuario_id",
                table: "acceso_emergencia",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_codigo_recuperacion_usuario_id_codigo_hash",
                table: "codigo_recuperacion",
                columns: new[] { "usuario_id", "codigo_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_desafio_token_hash",
                table: "mfa_desafio",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mfa_desafio_usuario_id",
                table: "mfa_desafio",
                column: "usuario_id");

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3): 02_basedatos.sql declara los
            // permisos por defecto FOR ROLE rent_owner, pero las tablas las crea rent_migrador, así
            // que hay que concederle sus permisos a rent_app explícitamente.
            // Estas tres tablas guardan secretos: la API solo las inserta y las actualiza (marcar un
            // desafío consumido o un código de recuperación gastado), nunca las borra.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON mfa_desafio TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON codigo_recuperacion TO rent_app;
                GRANT SELECT, INSERT, UPDATE ON acceso_emergencia TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "acceso_emergencia");

            migrationBuilder.DropTable(
                name: "codigo_recuperacion");

            migrationBuilder.DropTable(
                name: "mfa_desafio");

            migrationBuilder.DropColumn(
                name: "es_admin",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "mfa_secreto",
                table: "usuario");

            migrationBuilder.DropColumn(
                name: "modo_emergencia",
                table: "sesion");
        }
    }
}
