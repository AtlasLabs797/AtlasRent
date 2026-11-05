/** Importe escrito a mano ("1250.5" o "1250,5") como texto decimal con punto; `null` si no es un importe (máx. 2 decimales). */
export function importeValido(texto: string): string | null {
  const t = texto.trim().replace(',', '.');
  return /^\d+(\.\d{1,2})?$/.test(t) ? t : null;
}
