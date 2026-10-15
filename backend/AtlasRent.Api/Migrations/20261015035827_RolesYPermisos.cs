using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class RolesYPermisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rol",
                columns: table => new
                {
                    clave = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol", x => x.clave);
                });

            migrationBuilder.CreateTable(
                name: "usuario_permiso",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    permitido = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_permiso", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_permiso_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_rol",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_clave = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_rol", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_rol_usuario_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "permiso_rol",
                columns: table => new
                {
                    rol_clave = table.Column<string>(type: "character varying(32)", nullable: false),
                    accion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permiso_rol", x => new { x.rol_clave, x.accion });
                    table.ForeignKey(
                        name: "fk_permiso_rol_rol_rol_clave",
                        column: x => x.rol_clave,
                        principalTable: "rol",
                        principalColumn: "clave",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "rol",
                columns: new[] { "clave", "nombre" },
                values: new object[,]
                {
                    { "admin", "Administrador" },
                    { "empleado", "Empleado" },
                    { "gerente", "Gerente" }
                });

            migrationBuilder.InsertData(
                table: "permiso_rol",
                columns: new[] { "accion", "rol_clave" },
                values: new object[,]
                {
                    { "auditoria.ver", "admin" },
                    { "cliente.editar", "admin" },
                    { "cliente.ver", "admin" },
                    { "cobro.registrar", "admin" },
                    { "configuracion.editar", "admin" },
                    { "contrato.borrador", "admin" },
                    { "contrato.estado", "admin" },
                    { "cotizacion.enviar", "admin" },
                    { "cuenta.desbloquear", "admin" },
                    { "documento.enviar", "admin" },
                    { "factura.anular", "admin" },
                    { "factura.borrador", "admin" },
                    { "factura.emitir", "admin" },
                    { "factura.ver", "admin" },
                    { "fianza.mover", "admin" },
                    { "informe.ver", "admin" },
                    { "inmueble.documento", "admin" },
                    { "ip.desbloquear", "admin" },
                    { "mensajeria.masivo", "admin" },
                    { "mfa.obligatorio", "admin" },
                    { "plantilla.editar", "admin" },
                    { "seguridad.acceso_emergencia", "admin" },
                    { "sesion.cerrar", "admin" },
                    { "sesion.ver", "admin" },
                    { "cliente.editar", "empleado" },
                    { "cliente.ver", "empleado" },
                    { "contrato.borrador", "empleado" },
                    { "cotizacion.enviar", "empleado" },
                    { "factura.borrador", "empleado" },
                    { "factura.ver", "empleado" },
                    { "sesion.cerrar", "empleado" },
                    { "sesion.ver", "empleado" },
                    { "auditoria.ver", "gerente" },
                    { "cliente.editar", "gerente" },
                    { "cliente.ver", "gerente" },
                    { "cobro.registrar", "gerente" },
                    { "contrato.borrador", "gerente" },
                    { "contrato.estado", "gerente" },
                    { "cotizacion.enviar", "gerente" },
                    { "documento.enviar", "gerente" },
                    { "factura.anular", "gerente" },
                    { "factura.borrador", "gerente" },
                    { "factura.emitir", "gerente" },
                    { "factura.ver", "gerente" },
                    { "fianza.mover", "gerente" },
                    { "informe.ver", "gerente" },
                    { "inmueble.documento", "gerente" },
                    { "mensajeria.masivo", "gerente" },
                    { "sesion.cerrar", "gerente" },
                    { "sesion.ver", "gerente" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_usuario_permiso_usuario_id_accion",
                table: "usuario_permiso",
                columns: new[] { "usuario_id", "accion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_rol_usuario_id",
                table: "usuario_rol",
                column: "usuario_id",
                unique: true);

            // F1-07: el indicador provisional de administrador de F1-06 pasa al modelo de roles.
            // Los que no eran administradores se quedan como empleados: es el techo más bajo, así
            // que nadie gana permiso por el cambio. INSERT en vez de UPDATE porque cada usuario
            // empieza sin fila en usuario_rol.
            migrationBuilder.Sql(
                """
                INSERT INTO usuario_rol (id, usuario_id, rol_clave, created_at)
                SELECT gen_random_uuid(), u.id, CASE WHEN u.es_admin THEN 'admin' ELSE 'empleado' END, now()
                FROM usuario u;
                """);

            // Con la información ya volcada, el indicador es redundante y se va.
            migrationBuilder.DropColumn(
                name: "es_admin",
                table: "usuario");

            // Permisos de la aplicación (docs/DOC_TECNICA.md §4.1). Es la primera migración en la que
            // rent_app necesita escribir: da de alta usuarios y les ajusta los permisos.
            migrationBuilder.Sql(
                """
                GRANT SELECT ON rol, permiso_rol TO rent_app;
                GRANT SELECT, INSERT, UPDATE, DELETE ON usuario_rol, usuario_permiso TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "es_admin",
                table: "usuario",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Se vuelve a derivar el indicador del rol, antes de tirar la tabla, para que la reversa
            // no pierda quién era administrador.
            migrationBuilder.Sql(
                """
                UPDATE usuario u SET es_admin = (ur.rol_clave = 'admin')
                FROM usuario_rol ur WHERE ur.usuario_id = u.id;
                """);

            migrationBuilder.DropTable(
                name: "permiso_rol");

            migrationBuilder.DropTable(
                name: "usuario_permiso");

            migrationBuilder.DropTable(
                name: "usuario_rol");

            migrationBuilder.DropTable(
                name: "rol");
        }
    }
}
