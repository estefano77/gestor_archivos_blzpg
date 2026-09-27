using Microsoft.EntityFrameworkCore;
using GestorArchivosBlzpg.Data;

namespace GestorArchivosBlzpg.Api;

/// <summary>
/// Consulta de archivos con filtros, busqueda y orden.
/// </summary>
public class ConsultaArchivos(AppDbContext db)
{
    /// <summary>
    /// Aplica los filtros, la busqueda y el orden.
    /// </summary>
    /// <remarks>
    /// La busqueda se hace con ILIKE y ESCAPE, no con una expresion regular.
    /// La version con MongoDB construia <c>new RegExp(entrada)</c> con lo que
    /// escribiera la persona, de modo que un punto o un parentesis cambiaban el
    /// patron: buscar "informe (2024)" no encontraba "informe 2024" y, peor,
    /// un patron sin cerrar podia llegar a fallar. Con ILIKE, lo que escribe la
    /// persona se compara literalmente.
    ///
    /// El ESCAPE '\' es lo que hace posible: sin el, un % en lo que se escribe
    /// sigue siendo un comodin y el usuario buscaria patrones en vez de
    /// texto.
    /// </remarks>
    public async Task<List<FileItem>> Buscar(Guid userId, Filtros filtros, CancellationToken ct = default)
    {
        var q = db.Files.Where(f => f.UserId == userId);

        if (filtros.CarpetaId is { } carpetaId)
        {
            q = q.Where(f => f.FolderId == carpetaId);
        }

        if (!string.IsNullOrWhiteSpace(filtros.Categoria))
        {
            var cat = filtros.Categoria.ToLowerInvariant();
            q = q.Where(f => f.Category == cat);
        }

        if (!string.IsNullOrWhiteSpace(filtros.Busqueda))
        {
            // Se escapan los comodines de LIKE antes de usarlos como texto.
            var patron = Escapar(filtros.Busqueda.Trim());

            q = q.Where(f =>
                EF.Functions.ILike(f.OriginalName, $"%{patron}%", "\\")
                || EF.Functions.ILike(f.Description, $"%{patron}%", "\\")
                || f.Tags.Any(t => EF.Functions.ILike(t, $"%{patron}%", "\\")));
        }

        q = filtros.Orden?.ToLowerInvariant() switch
        {
            "name" => filtros.Desc ? q.OrderByDescending(f => f.OriginalName) : q.OrderBy(f => f.OriginalName),
            "size" => filtros.Desc ? q.OrderByDescending(f => f.Size) : q.OrderBy(f => f.Size),
            "category" => filtros.Desc ? q.OrderByDescending(f => f.Category) : q.OrderBy(f => f.Category),
            "createdat" => filtros.Desc ? q.OrderByDescending(f => f.CreatedAt) : q.OrderBy(f => f.CreatedAt),
            // Por defecto, lo mas reciente primero: es lo que espera alguien
            // que acaba de subir algo.
            _ => filtros.Desc ? q.OrderBy(f => f.CreatedAt).ThenBy(f => f.OriginalName)
                               : q.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.OriginalName),
        };

        return await q.ToListAsync(ct);
    }

    /// <summary>
    /// Escapa los caracteres que en LIKE significan algo.
    /// </summary>
    /// <remarks>
    /// Sin esto, escribir <c>100%</c> buscaria "100" seguido de cualquier cosa,
    /// y escribir <c>a_b</c> trataria la barra baja como comodin de un solo
    /// caracter. El usuario quiere buscar esos simbolos, no patrones.
    /// </remarks>
    private static string Escapar(string texto) => texto
        .Replace("\\", "\\\\")   // la propia barra, primero
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
