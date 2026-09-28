// Tema claro u oscuro.
//
// La preferencia se guarda en el almacenamiento del navegador porque es lo unico
// que sobrevive a una recarga. No es un dato sensible: es una preferencia de
// aspecto, y por eso puede vivir ahi sin más.
//
// El atributo dark va en el elemento raiz (documentElement) y no en el cuerpo.
// Los estilos del tema oscuro se escriben como ".dark .algo", y con la clase en
// el cuerpo habria que repetir el selector en cada regla.

const CLAVE = "tema";
const CLARO = "claro";
const OSCURO = "oscuro";

/**
 * Devuelve el tema guardado, o el que siga al sistema si no hay ninguno.
 */
export function obtenerTema() {
    const guardado = localStorage.getItem(CLAVE);

    if (guardado === OSCURO || guardado === CLARO) {
        return guardado;
    }

    // Sin preferencia guardada, se sigue al sistema. Se comprueba si el sistema
    // pide tema oscuro; si no, claro.
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? OSCURO : CLARO;
}

/**
 * Pone el tema en el documento y lo guarda.
 */
export function aplicarTema(tema) {
    const oscuro = tema === OSCURO;

    // La clase, no el atributo, porque es lo que espera el CSS. Se pone en
    // documentElement y no en body para que las reglas ".dark .x" apliquen a
    // todo el documento.
    document.documentElement.classList.toggle("dark", oscuro);
    localStorage.setItem(CLAVE, oscuro ? OSCURO : CLARO);

    return oscuro ? OSCURO : CLARO;
}
