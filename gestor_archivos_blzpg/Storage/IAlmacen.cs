namespace GestorArchivosBlzpg.Storage;

/// <summary>Resultado de una descarga desde el almacen.</summary>
public sealed record Binario(string Nombre, Stream Contenido, string ContentType, long Tamano);

/// <summary>
/// Donde viven los binarios. La tabla de archivos guarda solo la ruta, nunca el
/// contenido.
/// </summary>
public interface IAlmacen
{
    /// <summary>
    /// Guarda un archivo y devuelve la ruta con la que volver a recuperarlo.
    /// </summary>
    Task<string> Guardar(string ruta, Stream contenido, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Abre un archivo para lectura. Lanza si la ruta no existe.
    /// </summary>
    Task<Binario> Leer(string ruta, CancellationToken ct = default);

    /// <summary>
    /// Borra un archivo. No falla si ya no esta: borrar lo que no existe
    /// significa que el estado final es el mismo.
    /// </summary>
    Task<bool> Borrar(string ruta, CancellationToken ct = default);

    /// <summary>
    /// Borra todo lo que cuelgue de un prefijo de carpeta. Se usa al eliminar
    /// una carpeta, para no dejar binarios huerfanos.
    /// </summary>
    Task<int> BorrarPrefijo(string prefijo, CancellationToken ct = default);
}
