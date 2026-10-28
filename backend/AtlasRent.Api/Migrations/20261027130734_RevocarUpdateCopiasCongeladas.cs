using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class RevocarUpdateCopiasCongeladas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BUG-052: la plantilla congelada, el inventario congelado y las versiones del contrato son
            // copias que se escriben una vez y no cambian. Se comprobó que el código no las actualiza
            // (solo las inserta y las lee), así que la aplicación no necesita UPDATE y es mejor
            // quitárselo: es la última barrera si algo intentara reescribir un contrato firmado.
            migrationBuilder.Sql(
                """
                REVOKE UPDATE ON contrato_plantilla_congelada, contrato_inventario_item, contrato_version FROM rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                GRANT UPDATE ON contrato_plantilla_congelada, contrato_inventario_item, contrato_version TO rent_app;
                """);
        }
    }
}
