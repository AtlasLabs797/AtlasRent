using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class PurgaIntentosLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SEC-030: intento_login guarda IP y nombres de acceso y no se purgaba nunca. La API solo
            // puede leer e insertar en esa tabla (es un registro de seguridad); borrar lo viejo lo hace
            // esta función SECURITY DEFINER, que corre con los permisos de quien creó la tabla y solo
            // sabe hacer eso. El mínimo de 30 días evita que se use para vaciar la tabla de la que
            // salen los bloqueos.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION purgar_intentos_login(p_dias integer) RETURNS bigint
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp AS $$
                DECLARE
                  borrados bigint;
                BEGIN
                  IF p_dias < 30 THEN
                    RAISE EXCEPTION 'La retencion minima de intento_login es de 30 dias';
                  END IF;

                  DELETE FROM intento_login WHERE ocurrido_at < now() - make_interval(days => p_dias);
                  GET DIAGNOSTICS borrados = ROW_COUNT;
                  RETURN borrados;
                END
                $$;

                REVOKE ALL ON FUNCTION purgar_intentos_login(integer) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION purgar_intentos_login(integer) TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purgar_intentos_login(integer);");
        }
    }
}
