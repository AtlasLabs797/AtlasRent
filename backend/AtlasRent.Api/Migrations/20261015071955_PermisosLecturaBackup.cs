using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class PermisosLecturaBackup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F1-12: permisos de lectura de rent_backup. Sin ellos, el pg_dump de la copia de
            // seguridad falla (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
            //
            // db/scripts/02_basedatos.sql declara los permisos por defecto FOR ROLE rent_owner, pero
            // las tablas las crea rent_migrador, que solo es MIEMBRO de rent_owner y no su
            // propietario. Por eso esos permisos por defecto nunca se aplicaron a ninguna tabla y
            // hay que concederlos tabla a tabla, como ya hace cada migración con rent_app. Aquí
            // además se corrige la causa para las tablas futuras, declarando los permisos por
            // defecto del rol que realmente las crea.
            migrationBuilder.Sql(
                """
                GRANT SELECT ON ALL TABLES IN SCHEMA public TO rent_backup;
                GRANT SELECT ON ALL SEQUENCES IN SCHEMA public TO rent_backup;

                ALTER DEFAULT PRIVILEGES FOR ROLE rent_migrador IN SCHEMA public
                  GRANT SELECT ON TABLES TO rent_backup;
                ALTER DEFAULT PRIVILEGES FOR ROLE rent_migrador IN SCHEMA public
                  GRANT SELECT ON SEQUENCES TO rent_backup;
                """);

            // OJO: con esto NO basta para las tablas con RLS (auditoria y documento_inmueble), y no
            // se puede arreglar con una política. pg_dump saca los datos con
            // "COPY tabla (...) TO stdout", y PostgreSQL rechaza esa copia sobre una tabla con RLS
            // para un rol que no la ignora, con el error "query would be affected by row-level
            // security policy" (SQLSTATE 55000). No hay política que lo evite: el bloqueo es de la
            // sentencia COPY, no de qué filas devuelva.
            //
            // La solución es que rent_backup ignore la RLS (ALTER ROLE rent_backup BYPASSRLS), y eso
            // solo puede hacerlo un superusuario: rent_migrador no tiene CREATEROLE y falla con
            // "permission denied to alter role". El rol es de solo lectura y está dedicado a las
            // copias, que es exactamente el caso para el que BYPASSRLS existe.
            //
            // TODO(A#): añadir "ALTER ROLE rent_backup BYPASSRLS;" a db/scripts/01_roles.sql, junto
            // al CREATE ROLE. Hasta que esté, el pg_dump falla al llegar a 'auditoria' y la copia no
            // se genera. Los tests de F1-12 lo dejan demostrado.
            //
            // Esta migración no crea ninguna tabla, así que no tiene permisos que conceder a
            // rent_app: los de las tablas existentes no cambian (docs/DOC_TECNICA.md §4.1).
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER DEFAULT PRIVILEGES FOR ROLE rent_migrador IN SCHEMA public
                  REVOKE SELECT ON TABLES FROM rent_backup;
                ALTER DEFAULT PRIVILEGES FOR ROLE rent_migrador IN SCHEMA public
                  REVOKE SELECT ON SEQUENCES FROM rent_backup;
                """);
        }
    }
}