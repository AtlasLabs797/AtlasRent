import { useEffect, useState } from 'react';

/** Valor que se actualiza `ms` después del último cambio: evita una petición por pulsación. */
export function useRetardo<T>(valor: T, ms: number): T {
  const [retardado, setRetardado] = useState(valor);
  useEffect(() => {
    const id = setTimeout(() => setRetardado(valor), ms);
    return () => clearTimeout(id);
  }, [valor, ms]);
  return retardado;
}
