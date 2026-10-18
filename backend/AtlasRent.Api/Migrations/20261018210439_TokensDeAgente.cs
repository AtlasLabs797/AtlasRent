using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class TokensDeAgente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F6-01: credenciales de los agentes externos. Del token solo va el hash SHA-256 (el
            // en claro sale una vez, en la respuesta del alta) y las acciones que puede pedir, que
            // nunca amplían el techo del propietario.
            migrationBuilder.CreateTable(
                name: "agente_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    hash_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    acciones = table.Column<string[]>(type: "text[]", nullable: false),
                    creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    caduca = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_uso = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    limite_consultas_dia = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agente_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_agente_token_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "permiso_rol",
                columns: new[] { "accion", "rol_clave" },
                values: new object[,]
                {
                    { "agente.token", "admin" },
                    { "ia.consultar", "admin" },
                    { "ia.proponer", "admin" },
                    { "ia.consultar", "gerente" },
                    { "ia.proponer", "gerente" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_agente_token_hash_token",
                table: "agente_token",
                column: "hash_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agente_token_usuario_id",
                table: "agente_token",
                column: "usuario_id");

            // Sin DELETE, como el resto de las tablas de F1 en adelante: revocar es poner la fecha,
            // nunca borrar la fila, para que la auditoría siga explicando qué token hubo (AGENTS.md §6).
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON agente_token TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agente_token");

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "agente.token", "admin" });

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "ia.consultar", "admin" });

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "ia.proponer", "admin" });

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "ia.consultar", "gerente" });

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "ia.proponer", "gerente" });
        }
    }
}
