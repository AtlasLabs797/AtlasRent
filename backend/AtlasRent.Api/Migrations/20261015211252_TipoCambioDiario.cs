using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class TipoCambioDiario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F3-01: el tipo de cambio diario (docs/ESPECIFICACION.md §3.12, D9 y D22).
            //
            // `xmin` no se crea a propósito, igual que en las tablas de F2-08: es una columna de
            // sistema de PostgreSQL, ya existe en la tabla y crearla da error. El modelo la sigue
            // teniendo y el proveedor la usa sola en el WHERE de cada UPDATE.
            migrationBuilder.CreateTable(
                name: "tipo_cambio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    moneda = table.Column<int>(type: "integer", nullable: false),
                    valor = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    fuente = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipo_cambio", x => x.id);
                    // El dólar es la base y vale 1 por definición: no se guarda, y una fila suya
                    // solo podría contradecirlo. Un valor no positivo rompería toda conversión y
                    // dividiría por cero al consolidar.
                    table.CheckConstraint("ck_tipo_cambio_no_usd", "moneda <> 1");
                    table.CheckConstraint("ck_tipo_cambio_valor_positivo", "valor > 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tipo_cambio_fecha_moneda",
                table: "tipo_cambio",
                columns: new[] { "fecha", "moneda" },
                unique: true);

            // F3-01: la tabla la crea rent_migrador y 02_basedatos.sql solo declara permisos por
            // defecto para rent_owner (docs/DOC_TECNICA.md §4.3), así que sin esta línea la migración
            // aplica bien y la aplicación falla en runtime con "permission denied" al cargar el
            // cambio.
            //
            // UPDATE sí entra, y a propósito: un valor vuelve a escribirse cada vez que la fuente lo
            // refresca o cuando alguien lo corrige a mano. DELETE no entra, y no es un olvido: el tipo
            // de cambio es un dato económico congelado en facturas y cobros (D9), así que la fila que
            // explica qué se usó ese día no se borra nunca (AGENTS.md §6). Si un valor está mal, se
            // corrige para el día siguiente y la fila vieja se queda diciendo lo que decía.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON tipo_cambio TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tipo_cambio");
        }
    }
}
