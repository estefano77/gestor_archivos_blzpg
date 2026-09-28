using GestorArchivosBlzpg.Comun;
using System.Security.Claims;
using GestorArchivosBlzpg.Config;
using GestorArchivosBlzpg.Data;
using GestorArchivosBlzpg.Storage;
using Microsoft.EntityFrameworkCore;

namespace GestorArchivosBlzpg.Api;

/// <summary>
/// Rutas de archivos: listar, subir, editar, mover, descargar y previsualizar.
/// </summary>
public static class RutasArchivos
{
    /// <summary>Lista los archivos del usuario, con filtros.</summary>
    public static async Task<IResult> Listar(
        HttpContext http,
        AppDbContext db,
        ConsultaArchivos consulta,
        Guid? carpetaId,
        string? categoria,
        string? busqueda,
        string? orden,
        bool? desc)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var filtros = new Filtros(
            carpetaId,
            categoria,
            busqueda,
            orden,
            desc ?? false);

        var archivos = await consulta.Buscar(userId, filtros, http.RequestAborted);

        // Los nombres de carpeta se resuelven en una sola consulta, no una por
        // archivo. Con 200 archivos, resolverlos de uno en uno serian 200 viajes
        // a la base que se notaban.
        var nombres = await db.Folders
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, http.RequestAborted);

        return Results.Json(new ListaArchivosDto
        {
            Files = [.. archivos.Select(f => Dto.Archivo(
                f,
                f.FolderId is { } id && nombres.TryGetValue(id, out var n) ? n : null))],
        });
    }

    /// <summary>Resumen de espacio usado.</summary>
    public static async Task<IResult> Estadisticas(
        HttpContext http, AppDbContext db, ServicioArchivos servicio, Configuracion config)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var usados = await servicio.EspacioUsado(userId);
        var cuota = AppLimits.UserQuotaBytes(config);
        var ratio = AppLimits.QuotaWarnRatio(config);

        var porCategoria = await db.Files
            .Where(f => f.UserId == userId)
            .GroupBy(f => f.Category)
            .Select(g => new { categoria = g.Key, archivos = g.Count(), bytes = g.Sum(x => x.Size) })
            .ToListAsync();

        var total = await db.Files.CountAsync(f => f.UserId == userId);
        var carpetas = await db.Folders.CountAsync(c => c.UserId == userId);

        return Results.Json(new
        {
            totalFiles = total,
            totalBytes = usados,
            totalFolders = carpetas,
            quotaBytes = cuota,
            warnBytes = (long)(cuota * ratio),
            usedRatio = cuota > 0 ? (double)usados / cuota : 0,
            byCategory = porCategoria,
        });
    }

    /// <summary>
    /// Sube un archivo.
    /// </summary>
    /// <remarks>
    /// El orden es el importante: primero se comprueba el tamano (que no necesita
    /// base de datos y evita subir 30 MB para nada), despues se reserva la
    /// cuota con su transaccion, y solo si eso sale bien se guarda el binario.
    ///
    /// Si se guardara el binario antes de reservar, un rechazo por cuota
    /// dejaria el archivo en el almacen sin fila que lo referencia, es decir,
    /// un huerfano que ocupa espacio y que nadie sabe quitar. En la version
    /// anterior se-borraron 13 de estos.
    /// </remarks>
    public static async Task<IResult> Subir(
        HttpContext http,
        AppDbContext db,
        ServicioArchivos servicio,
        IAlmacen almacen,
        Configuracion config,
        IFormFile? archivo,
        CancellationToken ct)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        // Los campos de texto de un multipart no se enlazan como parametros
        // sueltos del handler: llegan en Request.Form, y si se piden como
        // parametros opcionales llegan vacios sin avisar. Por eso se leen de ahi.
        // Sin esto, la descripcion y las etiquetas se perdian en silencio y la
        // subida devolvia 201 como si se hubieran guardado.
        var formulario = http.Request.HasFormContentType
            ? await http.Request.ReadFormAsync(ct)
            : null;

        var descripcion = formulario?["description"].ToString();
        var etiquetas = formulario?["tags"].ToString();
        var carpeta = formulario?["folderId"].ToString();

        Guid? folderId = null;
        if (!string.IsNullOrWhiteSpace(carpeta) && Guid.TryParse(carpeta, out var f))
        {
            folderId = f;
        }

        if (archivo is null || archivo.Length == 0)
        {
            return Respuestas.Error("No se recibio ningun archivo.", StatusCodes.Status400BadRequest);
        }

        var maximo = AppLimits.MaxFileSize(config);
        if (archivo.Length > maximo)
        {
            var mb = archivo.Length / (1024.0 * 1024.0);
            return Results.Json(
                new
                {
                    message = $"El archivo supera el limite de {maximo / (1024 * 1024)} MB permitido " +
                              $"(Tamano: {mb:0.0} MB)",
                },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        // La carpeta, si la hay, tiene que ser de este usuario. Sin esta
        // comprobacion se podrian colgar archivos en la carpeta de otro.
        if (folderId is { } fid)
        {
            var esSuya = await db.Folders.AnyAsync(c => c.Id == fid && c.UserId == userId, ct);
            if (!esSuya)
            {
                return Respuestas.NoEncontrado();
            }
        }

        var nombre = Path.GetFileName(archivo.FileName);
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Respuestas.Error("El nombre del archivo no es valido.", StatusCodes.Status400BadRequest);
        }

        // El nombre se guarda como va. Lo que no se guarda es la ruta: la del
        // almacen la genera el servidor, y si se aceptara la del cliente podria
        // escribir fuera de su carpeta.
        var registro = new FileItem
        {
            OriginalName = nombre,
            MimeType = string.IsNullOrWhiteSpace(archivo.ContentType) ? "application/octet-stream" : archivo.ContentType,
            Category = Categorias.Deducir(archivo.ContentType, nombre),
            Size = archivo.Length,
            StoragePath = RutasAlmacen.Para(userId, Guid.NewGuid(), nombre),
            Description = descripcion?.Trim() ?? "",
            Tags = ParsearEtiquetas(etiquetas),
            FolderId = folderId,
        };

        var resultado = await servicio.ReservarYCrear(userId, registro, AppLimits.UserQuotaBytes(config), ct);

        if (!resultado.Aceptado)
        {
            return Results.Json(
                new
                {
                    message = $"No cabe en tu cuota. Ocupas " +
                              $"{resultado.UsadoAntes / (1024.0 * 1024.0):0.00} MB de " +
                              $"{resultado.Cuota / (1024 * 1024)} MB y este archivo ocupa " +
                              $"{archivo.Length / (1024.0 * 1024.0):0.00} MB.",
                },
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        await using (var flujo = archivo.OpenReadStream())
        {
            await almacen.Guardar(registro.StoragePath, flujo, registro.MimeType, ct);
        }

        return Results.Json(new { message = "Archivo subido", file = Dto.Archivo(registro, null) },
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>Edita nombre, descripcion, etiquetas o carpeta de un archivo.</summary>
    public static async Task<IResult> Editar(
        HttpContext http, AppDbContext db, Guid id, EditarArchivo body)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var archivo = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
        if (archivo is null) return Respuestas.NoEncontrado();

        if (!string.IsNullOrWhiteSpace(body.Name))
        {
            var nombre = body.Name.Trim();
            if (nombre.Length > 255) return Respuestas.Error("El nombre es demasiado largo.", 400);

            var repetido = await db.Files.AnyAsync(
                f => f.UserId == userId && f.Id != id && f.OriginalName.ToLower() == nombre.ToLower());

            if (repetido) return Respuestas.Error("Ya hay un archivo con ese nombre.", StatusCodes.Status409Conflict);

            archivo.OriginalName = nombre;
        }

        if (body.Description is not null) archivo.Description = body.Description.Trim();
        if (body.Tags is not null) archivo.Tags = body.Tags;
        if (body.FolderId is not null) archivo.FolderId = body.FolderId;

        await db.SaveChangesAsync();
        return Results.Json(new { message = "Archivo actualizado", file = Dto.Archivo(archivo, null) });
    }

    /// <summary>
    /// Elimina un archivo: su fila y su binario.
    /// </summary>
    public static async Task<IResult> Eliminar(HttpContext http, AppDbContext db, IAlmacen almacen, Guid id)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var archivo = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
        if (archivo is null) return Respuestas.NoEncontrado();

        db.Files.Remove(archivo);
        await db.SaveChangesAsync();

        // El binario se borra despues de la fila, y su fallo no se propaga: la
        // fila ya no esta, asi que el usuario ya no lo ve, y dejar el error sin
        // avisar haria que pareciera que no se pudo borrar.
        try
        {
            await almacen.Borrar(archivo.StoragePath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Aviso: no se pudo borrar el binario {archivo.StoragePath}: {ex.Message}");
        }

        return Results.Json(new { message = "Archivo eliminado" });
    }

    /// <summary>
    /// Devuelve el binario con su nombre en la cabecera.
    /// </summary>
    /// <remarks>
    /// El Content-Disposition va en modo attachment, que es lo que hace que el
    /// navegador lo descargue en vez de intentar abrirlo. Con inline, un PDF se
    /// abriria en una pestana y el usuario perderia la vista de la aplicacion.
    /// </remarks>
    public static async Task<IResult> Descargar(HttpContext http, AppDbContext db, IAlmacen almacen, Guid id)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var archivo = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
        if (archivo is null) return Respuestas.NoEncontrado();

        try
        {
            var binario = await almacen.Leer(archivo.StoragePath, http.RequestAborted);
            return Results.File(binario.Contenido, binario.ContentType, archivo.OriginalName);
        }
        catch (FileNotFoundException)
        {
            return Results.Json(
                new { message = "El archivo ya no esta en el almacen." },
                statusCode: StatusCodes.Status410Gone);
        }
    }

    /// <summary>Resumen para la vista previa.</summary>
    public static async Task<IResult> Previsualizar(HttpContext http, AppDbContext db, IAlmacen almacen, Guid id)
    {
        if (Respuestas.SinSesion(http.User, out var userId) is { } sin) return sin;

        var archivo = await db.Files.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
        if (archivo is null) return Respuestas.NoEncontrado();

        return Results.Json(new { file = Dto.Archivo(archivo, null) });
    }

    private static string[] ParsearEtiquetas(string? tags)
        => string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record EditarArchivo(string? Name, string? Description, string[]? Tags, Guid? FolderId);
