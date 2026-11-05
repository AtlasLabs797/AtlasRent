// DESIGN.md §3.1 / DOC_TECNICA §3.1: fija data-theme antes del primer pintado para que no haya parpadeo.
// Va en un archivo propio y no en línea para que la CSP del backend no necesite 'unsafe-inline' en script-src.
// La clave y los valores deben coincidir con src/tema/resolver.ts (lo comprueba tests/tema.test.ts).
(function () {
  try {
    var preferencia = localStorage.getItem('atlas-rent.tema') || 'sistema';
    var oscuro =
      preferencia === 'oscuro' ||
      (preferencia === 'sistema' && window.matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.setAttribute('data-theme', oscuro ? 'dark' : 'light');
  } catch (e) {
    document.documentElement.setAttribute('data-theme', 'light');
  }
})();
