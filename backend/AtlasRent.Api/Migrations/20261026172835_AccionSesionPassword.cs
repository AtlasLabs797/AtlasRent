using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccionSesionPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "permiso_rol",
                columns: new[] { "accion", "rol_clave" },
                values: new object[,]
                {
                    { "sesion.password", "admin" },
                    { "sesion.password", "empleado" },
                    { "sesion.password", "gerente" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "sesion.password", "admin" });

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "sesion.password", "empleado" });

            migrationBuilder.DeleteData(
                table: "permiso_rol",
                keyColumns: new[] { "accion", "rol_clave" },
                keyValues: new object[] { "sesion.password", "gerente" });
        }
    }
}
