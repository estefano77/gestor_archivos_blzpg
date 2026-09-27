using GestorArchivosBlzpg.Comun;
using GestorArchivosBlzpg.Data;

namespace GestorArchivosBlzpg.Api;

/// <summary>
/// Conversion de entidades a los tipos que ve el navegador.
/// </summary>
/// <remarks>
/// Vive en un solo sitio a proposito. Antes se armaban objetos anonimos
/// dispersos por las rutas, y cada uno escribia las claves a mano; un
/// cambio de nombre obligaba a acordarse de todos. Con los contratos en el
/// proyecto comun, servidor y cliente comparten las mismas clases.
/// </remarks>
public static class Dto
{
    public static ArchivoDto Archivo(FileItem f, string? nombreCarpeta) => new()
    {
        Id = f.Id,
        OriginalName = f.OriginalName,
        MimeType = f.MimeType,
        Category = f.Category,
        Size = f.Size,
        Folder = nombreCarpeta,
        FolderId = f.FolderId,
        Description = f.Description,
        Tags = f.Tags,
        CreatedAt = f.CreatedAt,
        UpdatedAt = f.UpdatedAt,
    };

    public static CarpetaDto Carpeta(Folder c, int conteo, long bytes) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Color = c.Color,
        Description = c.Description,
        FileCount = conteo,
        TotalBytes = bytes,
        CreatedAt = c.CreatedAt,
    };

    public static UsuarioDto Usuario(User u) => new()
    {
        Id = u.Id,
        Name = u.Name,
        Email = u.Email,
        Role = u.Role,
        AvatarColor = u.AvatarColor,
    };
}

/// <summary>
/// Filtros de la API de listar. Todos opcionales.
/// </summary>
public sealed record Filtros(
    Guid? CarpetaId,
    string? Categoria,
    string? Busqueda,
    string? Orden,
    bool Desc);
