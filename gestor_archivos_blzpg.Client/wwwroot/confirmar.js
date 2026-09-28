// Interoperabilidad con el navegador.
//
// Solo lo que Blazor no trae de serie, y todo pasa por un unico modulo para no
// repartir llamadas entre componentes.

window.confirmar = function (texto) {
    return confirm(texto);
};

// Cambia el titulo de la pestana.
//
// Se hace desde aqui y no con el componente <PageTitle> de Blazor porque este
// publica en el HeadOutlet, que usa un identificador de seccion fijo. Si dos
// paginas lo usan a la vez, Blazor falla con "There is already a subscriber to
// the content with the given section ID" y el render entero se cae. Desde fuera
// no se ve nada raro: la pagina aparece pintada y ningun boton responde.
//
// El texto llega como parametro, no como parte del codigo, de modo que un
// nombre de usuario con comillas o saltos de linea no rompe nada.
window.ponerTitulo = function (titulo) {
    document.title = titulo;
};
