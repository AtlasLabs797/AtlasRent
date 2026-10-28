using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProspectoConvertidoACliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cliente_id",
                table: "prospecto",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "convertido_at",
                table: "prospecto",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_prospecto_cliente_id",
                table: "prospecto",
                column: "cliente_id");

            migrationBuilder.AddForeignKey(
                name: "fk_prospecto_cliente_cliente_id",
                table: "prospecto",
                column: "cliente_id",
                principalTable: "cliente",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_prospecto_cliente_cliente_id",
                table: "prospecto");

            migrationBuilder.DropIndex(
                name: "ix_prospecto_cliente_id",
                table: "prospecto");

            migrationBuilder.DropColumn(
                name: "cliente_id",
                table: "prospecto");

            migrationBuilder.DropColumn(
                name: "convertido_at",
                table: "prospecto");
        }
    }
}
