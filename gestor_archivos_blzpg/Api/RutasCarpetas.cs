using GestorArchivosBlzpg.Data;
using GestorArchivosBlzpg.Storage;
using Microsoft.EntityFrameworkCore;

namespace GestorArchivosBlzpg.Api;

public sealed record NuevaCarpeta(string? Name, string? Description, string? Color);

public static class RutasCarpetas
{
    public static async Task<IResult> Listar(HttpContext http, ServicioCarpetas servicio)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var carpetas = await servicio.ListarConTotales(userId, http.RequestAborted);
        return Results.Json(new
        {
            folders = carpetas.Select(c => Dto.Carpeta(c.Carpeta, c.Conteo, c.Bytes)),
        });
    }

    public static async Task<IResult> Crear(HttpContext http, ServicioCarpetas servicio, NuevaCarpeta body)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var nombre = body.Name?.Trim();
        if (string.IsNullOrWhiteSpace(nombre)) return Respuestas.Error("El nombre no puede quedar vacio.", 400);

        if (nombre.Length > 60) return Respuestas.Error("El nombre no puede pasar de 60 caracteres.", 400);

        var carpeta = await servicio.Crear(
            userId, nombre, body.Description?.Trim() ?? "", body.Color ?? "indigo", http.RequestAborted);

        return Results.Json(new { message = "Carpeta creada", folder = Dto.Carpeta(carpeta, 0, 0) },
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Renombra una carpeta.
    /// </summary>
    public static async Task<IResult> Renombrar(
        HttpContext http, ServicioCarpetas servicio, Guid id, NuevaCarpeta body)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var nombre = body.Name?.Trim();
        if (string.IsNullOrWhiteSpace(nombre)) return Respuestas.Error("El nombre no puede quedar vacio.", 400);

        var carpeta = await servicio.Renombrar(id, userId, nombre, http.RequestAborted);
        if (carpeta is null) return Respuestas.NoEncontrado();

        return Results.Json(new { message = "Carpeta renombrada", folder = Dto.Carpeta(carpeta, 0, 0) });
    }

    /// <summary>
    /// Elimina una carpeta. Sus archivos se quedan sin carpeta, pero sus
    /// binarios se borran del almacen.
    /// </summary>
    /// <remarks>
    /// La base esta configurada con ON DELETE SET NULL a proposito: borrar una
    /// carpeta no debe llevarse por delante los documentos que habia dentro. En
    /// la version con MongoDB, en cambio, los archivos se buscaban por el
    /// <em>nombre</em> de la carpeta y se borraban con ellos, lo que ademas
    /// rompia si dos carpetas tenian el mismo nombre.
    ///
    /// Los binarios no estan en la base, asi que se quitan a mano. Por eso se
    /// recogen las rutas antes de borrar la carpeta: despues, el SET NULL ya no
    /// deja saber que archivos estaban dentro.
    /// </remarks>
    public static async Task<IResult> Eliminar(
        HttpContext http, AppDbContext db, ServicioCarpetas servicio, IAlmacen almacen, Guid id)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var carpeta = await servicio.Obtener(id, userId, http.RequestAborted);
        if (carpeta is null) return Respuestas.NoEncontrado();

        var rutas = await servicio.RutasParaBorrar(id, userId, http.RequestAborted);

        // ON DELETE SET NULL deja los archivos sin carpeta en vez de borrarlos,
        // que es lo que se quiere: perder la carpeta no deberia llevar por
        // delante documentos que habia dentro. Sus binarios si se quitan, y
        // para eso se han recogido antes las rutas: en cuanto la fila de la
        // carpeta desaparece, el SET NULL ya no permite saber quais eran.
        db.Folders.Remove(carpeta);
        await db.SaveChangesAsync();

        foreach (var ruta in rutas)
        {
            try
            {
                await almacen.Borrar(ruta, http.RequestAborted);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Aviso: no se pudo borrar el binario {ruta}: {ex.Message}");
            }
        }

        return Results.Json(new
        {
            message = $"Carpeta eliminada junto con {rutas.Count} archivo(s)",
            archivosEliminados = rutas.Count,
        });
    }
}
