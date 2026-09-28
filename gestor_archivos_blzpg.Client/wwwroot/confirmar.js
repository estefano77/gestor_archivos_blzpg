// Interoperabilidad con el navegador.
//
// Este archivo es un MODULO ES, y eso importa: Blazor lo carga con
// import("./confirmar.js") y despues llama a las funciones POR SU NOMBRE
// exportado. Declararlas como window.confirmar no sirve, porque un modulo no
// expone nada de lo que se cuelgue de window: la llamada falla con "Could not
// find 'confirmar' in the module".
//
// El fallo es silencioso y de los peores: la funcion de confirmar devolvia
// siempre que no, asi que al borrar un archivo no pasaba nada y ningun mensaje
// lo explicaba.

/**
 * Pregunta al usuario y devuelve si confirma.
 *
 * El texto llega como parametro, no interpolado en codigo, de modo que un
 * nombre de archivo con comillas o saltos de linea no rompe nada.
 */
export function confirmar(texto) {
    return window.confirm(texto);
}
