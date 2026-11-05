using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlertaVivaReabrible : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // BUG-008: la unicidad pasa de `clave` a `(tipo, clave)` y **parcial** a las alertas vivas
            // (`resuelta_at IS NULL`), que es lo que permite que una clave resuelta vuelva a levantarse.
            //
            // Aplica **con datos dentro** y sin tocar ninguno: `ix_alerta_clave` era único sobre
            // `clave`, así que las claves que ya había son todas distintas entre sí y, por tanto,
            // también lo son los pares `(tipo, clave)` que quedan sin resolver. No puede haber dos
            // filas vivas con el mismo par, y el índice se crea sin necesitar nada previo.
            //
            // Lo que sí cambia a partir de aquí es que el histórico puede acabar con más de una fila
            // cerrada por clave (eso es correcto: son episodios distintos del mismo asunto). Por eso
            // `ResolverAsync` solo cierra filas abiertas y `RegistrarAsync` reabre únicamente la más
            // reciente de las cerradas.
            migrationBuilder.DropIndex(
                name: "ix_alerta_clave",
                table: "alerta");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_tipo_clave",
                table: "alerta",
                columns: new[] { "tipo", "clave" },
                unique: true,
                filter: "resuelta_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // OJO: volver al único de `clave` a secas solo es posible si no hay dos filas —cerradas—
            // con la misma clave, que es justamente lo que la migración anterior deja pasar. Si las hay,
            // este `Down` falla y el histórico hay que resolverlo a mano (sin borrar: cerrando la más
            // antigua o renumerando su clave).
            migrationBuilder.DropIndex(
                name: "ix_alerta_tipo_clave",
                table: "alerta");

            migrationBuilder.CreateIndex(
                name: "ix_alerta_clave",
                table: "alerta",
                column: "clave",
                unique: true);
        }
    }
}
