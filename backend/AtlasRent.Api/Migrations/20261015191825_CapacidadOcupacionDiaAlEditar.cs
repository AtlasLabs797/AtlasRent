using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtlasRent.Api.Migrations
{
    /// <inheritdoc />
    public partial class CapacidadOcupacionDiaAlEditar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // El trigger de F2-01 contaba las filas vivas de la unidad en esa fecha, y al contar así
            // contaba también la fila que el propio UPDATE estaba tocando: dentro de un BEFORE ROW el
            // SELECT ve el estado anterior del UPDATE, así que la fila que se está reescribiendo
            // seguía saliendo en el recuento.
            //
            // El efecto era un falso positivo al editar una reserva corta: `SincronizarAsync` (F2-03)
            // no borra y reinserta, sino que hace UPDATE de la fila de `ocupacion_dia` cuando la fecha
            // sigue en la reserva. Pasar un medio día a día completo en la misma fecha hacía
            // `NEW.tipo = 2` con `ocupados = 1` (su propia fila), que el trigger leía como "ya está
            // ocupado y un día completo no cabe"; y al revés, de día completo a medio día, salía
            // "ya está reservado el día completo". En los dos casos la edición era imposible de
            // hacer, aunque la unidad estaba libre menos la propia reserva que se estaba moviendo.
            //
            // La corrección es excluir la fila del propio disparo (`d.id <> NEW.id`): la capacidad que
            // hay que comprobar es la que ocupan *las demás*. Con eso los tres casos siguen
            // funcionando igual para lo de verdad —un INSERT nunca ve la fila que no existe todavía— y
            // además sigue saltando cuando el día ya tiene los dos medios días o un día completo de
            // otra reserva o de un contrato.
            migrationBuilder.Sql(
                """
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

                  -- `d.id <> NEW.id`: cuenta lo que ocupan las otras filas, no la que se está
                  -- escribiendo. Solo cambia el UPDATE; en un INSERT NEW.id no está todavía en la
                  -- tabla y el resultado es el mismo.
                  SELECT count(*) FILTER (WHERE d.tipo = 2), count(*)
                  INTO completos, ocupados
                  FROM ocupacion_dia d
                  WHERE d.bloquea
                    AND d.id <> NEW.id
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
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volver a contar la fila propia. No es lo que había antes de F2-01 y sí era lo que
            // había al terminarlo, así que el Down devuelve exactamente eso.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION ocupacion_dia_comprueba_capacidad() RETURNS trigger
                LANGUAGE plpgsql AS $$
                DECLARE
                  completos integer;
                  ocupados integer;
                BEGIN
                  IF NOT NEW.bloquea THEN
                    RETURN NEW;
                  END IF;

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
                """);
        }
    }
}