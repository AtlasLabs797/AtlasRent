using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class FacturacionPeriodica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "periodo_fin",
                table: "factura",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "periodo_inicio",
                table: "factura",
                type: "date",
                nullable: true);

            // F3-05: una factura periódica por contrato, periodo y concepto (§3.8 «impide duplicados:
            // contrato, periodo, concepto»). Es lo que hace que «doble ejecución no duplica» sea
            // cierto en la base y no solo en el código.
            //
            // NULLS NOT DISTINCT (PostgreSQL 15) es lo que protege también a la factura de la
            // reserva corta, que no tiene contrato y por tanto llega con el contrato_id a nulo: sin
            // esto, PostgreSQL trataría cada nullo como distinto y la reserva sí se podría facturar
            // dos veces. El modelo de EF no expresa esa variante del índice —dice IsUnique()—, así
            // que si alguna migración reconstruye este índice hay que volver a escribirlo a mano.
            //
            // El filtro saca las facturas que no son periódicas (periodo_inicio nulo, que son todas
            // las tecleadas a mano) y las anuladas: una factura anulada libera su clave para poder
            // emitir la que la sustituye, y su número no vuelve atrás (D28).
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_factura_periodo_unico
                    ON factura (contrato_id, periodo_inicio, concepto)
                    NULLS NOT DISTINCT
                    WHERE periodo_inicio IS NOT NULL AND estado <> 3;
                """);

            // Sin GRANT aquí: factura ya existe y `rent_app` tiene SELECT, INSERT y UPDATE sobre
            // ella desde MigracionInicial. Los permisos son de tabla, no de columna, así que las dos
            // columnas nuevas entran dentro de lo que la aplicación ya podía hacer. Y nunca DELETE.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS ix_factura_periodo_unico;
                """);

            migrationBuilder.DropColumn(
                name: "periodo_fin",
                table: "factura");

            migrationBuilder.DropColumn(
                name: "periodo_inicio",
                table: "factura");
        }
    }
}
