using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class PlantillaPdfYDatosFacturadora : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direccion",
                table: "subempresa",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "subempresa",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rnc",
                table: "subempresa",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "telefono",
                table: "subempresa",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condiciones_pago",
                table: "serie",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "direccion",
                table: "subempresa");

            migrationBuilder.DropColumn(
                name: "email",
                table: "subempresa");

            migrationBuilder.DropColumn(
                name: "rnc",
                table: "subempresa");

            migrationBuilder.DropColumn(
                name: "telefono",
                table: "subempresa");

            migrationBuilder.DropColumn(
                name: "condiciones_pago",
                table: "serie");
        }
    }
}
