import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { repartirErrores } from './errores.ts';

/**
 * Pinta en un formulario de React Hook Form los errores de campo de un `ProblemDetails` y devuelve el
 * mensaje general que no pertenece a ningún campo (para un `Alert` encima del botón).
 *
 * `campos` son los nombres del formulario en camelCase; el backend manda PascalCase y `errores.ts` lo
 * convierte. Uso: `catch (e) { setGeneral(aplicarErroresApi(e, setError, ['contrasenaNueva'])) }`.
 */
export function aplicarErroresApi<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  campos: readonly Path<T>[],
  reserva = 'No se ha podido guardar. Inténtalo de nuevo.',
): string | null {
  const { porCampo, general } = repartirErrores(error, campos, reserva);
  for (const [campo, message] of Object.entries(porCampo)) {
    setError(campo as Path<T>, { type: 'servidor', message });
  }
  return general;
}
