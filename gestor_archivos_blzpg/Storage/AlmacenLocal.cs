using Microsoft.Extensions.Options;

namespace GestorArchivosBlzpg.Storage;

public class OpcionesAlmacenLocal
{
    public string Ruta { get; set; } = ".storage";
}

/// <summary>
/// Almacen en disco, para desarrollo.
/// </summary>
/// <remarks>
/// No sirve en un servidor de produccion: el disco de una instancia es local a
/// esa instancia, asi que con dos instancias o al reiniciar, los archivos se
/// pierden. Por eso el despliegue usa <c>STORAGE_BACKEND=supabase</c>.
/// Sirve para no depender de la nube al probar.
/// </remarks>
public class AlmacenLocal(IOptions<OpcionesAlmacenLocal> opciones) : IAlmacen
{
    private string Raiz => Path.GetFullPath(opciones.Value.Ruta);

    /// <summary>
    /// Resuelve la ruta dentro de la raiz, rechazando cualquier ruta que se
    /// salga.
    /// </summary>
    /// <remarks>
    /// La ruta la construye el servidor, no el cliente, pero conviene comprobarlo
    /// igualmente: un "../" en un nombre de archivo llevaria a leer o escribir
    /// fuera de la carpeta de almacenamiento. Se comprueba sobre la ruta ya
    /// resuelta, que es donde se ve el salto de verdad.
    /// </remarks>
    private string Resolver(string ruta)
    {
        var raiz = Raiz;
        var completa = Path.GetFullPath(Path.Combine(raiz, ruta));

        if (!completa.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && completa != raiz)
        {
            throw new InvalidOperationException(
                $"La ruta {ruta} se sale de la carpeta de almacenamiento.");
        }

        return completa;
    }

    public async Task<string> Guardar(string ruta, Stream contenido, string contentType, CancellationToken ct = default)
    {
        var completa = Resolver(ruta);
        Directory.CreateDirectory(Path.GetDirectoryName(completa)!);

        // FileMode.Create sobrescribe si ya existia un archivo con esa ruta.
        await using var fs = new FileStream(completa, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await contenido.CopyToAsync(fs, ct);

        return ruta;
    }

    public async Task<Binario> Leer(string ruta, CancellationToken ct = default)
    {
        var completa = Resolver(ruta);

        if (!File.Exists(completa))
        {
            throw new FileNotFoundException($"No existe el archivo {ruta} en el almacen local.");
        }

        var fs = new FileStream(completa, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

        return new Binario(Path.GetFileName(completa), fs, "application/octet-stream", fs.Length);
    }

    public Task<bool> Borrar(string ruta, CancellationToken ct = default)
    {
        var completa = Resolver(ruta);

        if (!File.Exists(completa)) return Task.FromResult(false);

        File.Delete(completa);
        return Task.FromResult(true);
    }

    public Task<int> BorrarPrefijo(string prefijo, CancellationToken ct = default)
    {
        var completa = Resolver(prefijo);
        if (!Directory.Exists(completa)) return Task.FromResult(0);

        var n = Directory.GetFiles(completa, "*", SearchOption.AllDirectories).Length;
        Directory.Delete(completa, recursive: true);
        return Task.FromResult(n);
    }
}
