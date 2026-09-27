using Microsoft.EntityFrameworkCore;

namespace GestorArchivosBlzpg.Data;

/// <summary>
/// Operaciones sobre carpetas.
/// </summary>
public class ServicioCarpetas(AppDbContext db)
{
    public async Task<List<Folder>> Listar(Guid userId, CancellationToken ct = default)
        => await db.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

    /// <summary>
    /// Carpeta con su recuento de archivos y sus bytes.
    /// </summary>
    /// <remarks>
    /// Se cuenta con una consulta agrupada y no con el numero de elementos de
    /// la coleccion cargada: cargar todos los archivos de todas las carpetas
    /// para contarlos es trabajo que se tira. Ademas <c>Count</c> en memoria
    /// dariaria 0 en vez de error cuando la carpeta esta vacia, que es el
    /// caso frecuente.
    /// </remarks>
    public async Task<List<(Folder Carpeta, int Conteo, long Bytes)>> ListarConTotales(
        Guid userId,
        CancellationToken ct = default)
    {
        var carpetas = await db.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync(ct);

        var totales = await db.Files
            .Where(f => f.UserId == userId && f.FolderId != null)
            .GroupBy(f => f.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Conteo = g.Count(), Bytes = g.Sum(x => x.Size) })
            .ToListAsync(ct);

        var indice = totales.ToDictionary(t => t.FolderId);

        return carpetas
            .Select(c => indice.TryGetValue(c.Id, out var t)
                ? (c, t.Conteo, t.Bytes)
                : (c, 0, 0L))
            .ToList();
    }

    public Task<Folder?> Obtener(Guid id, Guid userId, CancellationToken ct = default)
        => db.Folders.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId, ct);

    public async Task<Folder> Crear(Guid userId, string nombre, string descripcion, string color, CancellationToken ct = default)
    {
        var carpeta = new Folder
        {
            UserId = userId,
            Name = nombre,
            Description = descripcion,
            Color = color,
        };

        db.Folders.Add(carpeta);
        await db.SaveChangesAsync(ct);
        return carpeta;
    }

    /// <summary>
    /// Renombra una carpeta. No toca los archivos que contiene.
    /// </summary>
    /// <remarks>
    /// En la version con MongoDB, la carpeta era un texto repetido en cada
    /// archivo, y renombrarla obligaba a un updateMany sobre todos ellos. Con
    /// una clave foranea real, el nombre vive en un solo sitio y aqui basta un
    /// UPDATE de una fila. El nombre duplicado no puede aparecer porque el
    /// indice unico de la base lo impide.
    /// </remarks>
    public async Task<Folder?> Renombrar(Guid id, Guid userId, string nombre, CancellationToken ct = default)
    {
        var carpeta = await Obtener(id, userId, ct);
        if (carpeta is null) return null;

        carpeta.Name = nombre;
        await db.SaveChangesAsync(ct);
        return carpeta;
    }

    /// <summary>
    /// Archivos que hay que borrar del almacen al eliminar una carpeta.
    /// </summary>
    /// <remarks>
    /// Se consulta antes de borrar y se devuelve, porque los binarios no estan
    /// en la base. Si la base borrara la carpeta en cascada, las filas de los
    /// archivos desaparecerian y sus rutas con ellas, y los binarios se
    /// quedarian en el almacen sin nadie que los borre. Por eso la clave
    /// foranea esta en ON DELETE SET NULL y el borrado de archivos es
    /// explicito.
    /// </remarks>
    public async Task<List<string>> RutasParaBorrar(Guid carpetaId, Guid userId, CancellationToken ct = default)
        => await db.Files
            .Where(f => f.UserId == userId && f.FolderId == carpetaId)
            .Select(f => f.StoragePath)
            .ToListAsync(ct);

    public async Task<bool> Eliminar(Guid id, Guid userId, CancellationToken ct = default)
    {
        var carpeta = await Obtener(id, userId, ct);
        if (carpeta is null) return false;

        db.Folders.Remove(carpeta);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
