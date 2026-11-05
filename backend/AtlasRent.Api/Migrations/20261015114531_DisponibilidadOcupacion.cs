using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class DisponibilidadOcupacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ocupacion_dia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<int>(type: "integer", nullable: false),
                    origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    bloquea = table.Column<bool>(type: "boolean", nullable: false),
                    creado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    // La columna xmin la genera EF pero no se crea: es una columna de sistema de
                    // PostgreSQL (docs/DOC_TECNICA.md §4.3, aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ocupacion_dia", x => x.id);
                    table.ForeignKey(
                        name: "fk_ocupacion_dia_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ocupacion_rango",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    unidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origen = table.Column<int>(type: "integer", nullable: false),
                    origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: true),
                    bloquea = table.Column<bool>(type: "boolean", nullable: false),
                    creado_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                    // La columna xmin la genera EF pero no se crea (aviso heredado de F1-03).
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ocupacion_rango", x => x.id);
                    table.ForeignKey(
                        name: "fk_ocupacion_rango_unidad_unidad_id",
                        column: x => x.unidad_id,
                        principalTable: "unidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ocupacion_dia_origen_origen_id_fecha",
                table: "ocupacion_dia",
                columns: new[] { "origen", "origen_id", "fecha" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ocupacion_dia_unidad_id_fecha",
                table: "ocupacion_dia",
                columns: new[] { "unidad_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_ocupacion_rango_origen_origen_id",
                table: "ocupacion_rango",
                columns: new[] { "origen", "origen_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ocupacion_rango_unidad_id",
                table: "ocupacion_rango",
                column: "unidad_id");

            // Las reglas que solo PostgreSQL puede cumplir sin carreras (AGENTS.md §6 y
            // docs/ESPECIFICACION.md §5). La extensión btree_gist ya la crea la migración inicial;
            // aquí no se repite.
            //
            // SQLSTATE que se usan abajo, para que el servicio distinga un conflicto de
            // disponibilidad de cualquier otro error:
            //   23001 restrict_violation: lo lanzan los dos triggers, cuando el bloqueo choca con
            //         otro bloqueo de la misma unidad o con la capacidad del día.
            //   23P01 exclusion_violation: lo genera PostgreSQL al saltar la restricción de
            //         exclusión de ocupacion_rango.
            migrationBuilder.Sql(
                """
                -- Un contrato o una reserva mensual no puede solaparse con otro de la misma unidad.
                -- El predicado WHERE (bloquea) deja fuera las filas liberadas: se quedan en la
                -- tabla para poder auditar por qué un día estuvo ocupado, pero ya no chocan.
                -- El rango es [fecha_inicio, fecha_fin): lo que acaba el 31 deja libre el 1 siguiente.
                ALTER TABLE ocupacion_rango
                  ADD CONSTRAINT ex_ocupacion_rango_unidad
                  EXCLUDE USING gist (
                    unidad_id WITH =,
                    daterange(fecha_inicio, fecha_fin, '[)') WITH &&
                  )
                  WHERE (bloquea);

                -- Días sueltos: la unidad admite 2 medios días o 1 día completo
                -- (docs/ESPECIFICACION.md §3.4).
                -- Los números son TipoOcupacionDia: 1 = medio_dia, 2 = dia_completo. Si ese enum se
                -- reordena, hay que actualizar este SQL, que EF no genera.
                CREATE OR REPLACE FUNCTION ocupacion_dia_comprueba_capacidad() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                  completos integer;
                  ocupados integer;
                BEGIN
                  IF NOT NEW.bloquea THEN
                    RETURN NEW;
                  END IF;

                  -- Bloqueo por unidad. Es lo que serializa dos inserciones simultáneas del mismo
                  -- día: la segunda espera aquí a que la primera termine su transacción, y entonces
                  -- ve lo que esa escribió. Sin esta fila bloqueada, dos reservas a la vez
                  -- contarían las dos los mismos cero días vacíos y las dos pasarían.
                  PERFORM 1 FROM unidad WHERE id = NEW.unidad_id FOR UPDATE;

                  IF EXISTS (
                    SELECT 1
                    FROM ocupacion_rango r
                    WHERE r.bloquea
                      AND r.unidad_id = NEW.unidad_id
                      AND daterange(r.fecha_inicio, r.fecha_fin, '[)')
                          && daterange(NEW.fecha, NEW.fecha + 1, '[)')
                  ) THEN
                    RAISE EXCEPTION USING
                      ERRCODE = '23001',
                      MESSAGE = 'la unidad ya está ocupada por un contrato o una reserva mensual el '
                        || NEW.fecha;
                  END IF;

                  SELECT count(*) FILTER (WHERE d.tipo = 2), count(*)
                  INTO completos, ocupados
                  FROM ocupacion_dia d
                  WHERE d.bloquea
                    AND d.unidad_id = NEW.unidad_id
                    AND d.fecha = NEW.fecha;

                  IF NEW.tipo = 2 AND ocupados > 0 THEN
                    RAISE EXCEPTION USING
                      ERRCODE = '23001',
                      MESSAGE = 'el día ' || NEW.fecha || ' ya está ocupado y un día completo no cabe';
                  END IF;

                  IF NEW.tipo <> 2 AND completos > 0 THEN
                    RAISE EXCEPTION USING
                      ERRCODE = '23001',
                      MESSAGE = 'el día ' || NEW.fecha || ' ya está reservado el día completo';
                  END IF;

                  IF NEW.tipo <> 2 AND ocupados >= 2 THEN
                    RAISE EXCEPTION USING
                      ERRCODE = '23001',
                      MESSAGE = 'el día ' || NEW.fecha || ' ya tiene los dos medios días ocupados';
                  END IF;

                  RETURN NEW;
                END $$;

                DROP TRIGGER IF EXISTS trg_ocupacion_dia_capacidad ON ocupacion_dia;
                CREATE TRIGGER trg_ocupacion_dia_capacidad
                  BEFORE INSERT OR UPDATE ON ocupacion_dia
                  FOR EACH ROW EXECUTE FUNCTION ocupacion_dia_comprueba_capacidad();

                -- Y al revés: un rango que tapara días ya reservados sería un contrato solapado en
                -- silencio con reservas cortas. El mismo bloqueo por unidad, para que un rango y un
                -- día no puedan pasar los dos a la vez.
                CREATE OR REPLACE FUNCTION ocupacion_rango_comprueba_dias() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                  IF NOT NEW.bloquea THEN
                    RETURN NEW;
                  END IF;

                  PERFORM 1 FROM unidad WHERE id = NEW.unidad_id FOR UPDATE;

                  IF EXISTS (
                    SELECT 1
                    FROM ocupacion_dia d
                    WHERE d.bloquea
                      AND d.unidad_id = NEW.unidad_id
                      AND d.fecha >= NEW.fecha_inicio
                      AND (NEW.fecha_fin IS NULL OR d.fecha < NEW.fecha_fin)
                  ) THEN
                    RAISE EXCEPTION USING
                      ERRCODE = '23001',
                      MESSAGE = 'hay reservas cortas dentro del rango que empieza el '
                        || NEW.fecha_inicio;
                  END IF;

                  RETURN NEW;
                END $$;

                DROP TRIGGER IF EXISTS trg_ocupacion_rango_dias ON ocupacion_rango;
                CREATE TRIGGER trg_ocupacion_rango_dias
                  BEFORE INSERT OR UPDATE ON ocupacion_rango
                  FOR EACH ROW EXECUTE FUNCTION ocupacion_rango_comprueba_dias();
                """);

            // Aviso heredado de F1-03 (docs/DOC_TECNICA.md §4.3, l.136-137): las tablas las crea
            // rent_migrador y 02_basedatos.sql solo declara permisos por defecto para rent_owner,
            // así que cada migración nueva concede los suyos a rent_app. Sin esta línea la
            // migración aplica bien y la aplicación falla en runtime con "permission denied".
            // Estas filas no se borran: liberar es poner bloquea a false (AGENTS.md §6). Por eso el
            // GRANT no lleva DELETE, a diferencia de las migraciones anteriores: la aplicación no
            // necesita poder borrarlas y antes que dejar el permiso puesto está quitarlo.
            migrationBuilder.Sql(
                """
                GRANT SELECT, INSERT, UPDATE ON ocupacion_rango, ocupacion_dia TO rent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ocupacion_dia");

            migrationBuilder.DropTable(
                name: "ocupacion_rango");

            // Las funciones no se van solas al caer las tablas.
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS ocupacion_dia_comprueba_capacidad();
                DROP FUNCTION IF EXISTS ocupacion_rango_comprueba_dias();
                """);
        }
    }
}