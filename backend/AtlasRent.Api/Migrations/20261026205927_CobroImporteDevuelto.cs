using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class CobroImporteDevuelto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "importe_devuelto",
                table: "cobro",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_cobro_devuelto_acotado",
                table: "cobro",
                sql: "importe_devuelto >= 0 AND importe_devuelto <= importe_recibido + saldo_favor_usado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_cobro_devuelto_acotado",
                table: "cobro");

            migrationBuilder.DropColumn(
                name: "importe_devuelto",
                table: "cobro");
        }
    }
}
