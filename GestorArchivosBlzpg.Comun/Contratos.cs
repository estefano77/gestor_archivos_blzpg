using System.Text.Json.Serialization;

namespace GestorArchivosBlzpg.Comun;

/// <summary>
/// Forma de un archivo tal y como la recibe el navegador.
/// </summary>
/// <remarks>
/// Vive en el proyecto comun porque el servidor la produce y el cliente la
/// consume. Tenerla en un solo sitio evita el fallo tipico de este patron: que
/// el servidor serialice <c>OriginalName</c> como "OriginalName" y el cliente
/// lo busque como "originalName", y nada falle al compilar porque cada lado es
/// correcto por su cuenta.
///
/// El <c>[JsonPropertyName]</c> no es decoration por gusto: es lo que fija el
/// nombre en el JSON. Sin el, el nombre de la propiedad en el cable seria el de
/// C#.
/// </remarks>
public sealed record ArchivoDto
{
    [JsonPropertyName("_id")] public required Guid Id { get; init; }
    [JsonPropertyName("originalName")] public required string OriginalName { get; init; }
    [JsonPropertyName("mimeType")] public required string MimeType { get; init; }
    [JsonPropertyName("category")] public required string Category { get; init; }
    [JsonPropertyName("size")] public required long Size { get; init; }

    /// <summary>Nombre de la carpeta, o null si el archivo esta sin carpeta.</summary>
    [JsonPropertyName("folder")] public string? Folder { get; init; }

    /// <summary>Identificador de la carpeta, o null.</summary>
    [JsonPropertyName("folderId")] public Guid? FolderId { get; init; }

    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("tags")] public string[] Tags { get; init; } = [];
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("updatedAt")] public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Forma de una carpeta tal y como la recibe el navegador.</summary>
public sealed record CarpetaDto
{
    [JsonPropertyName("_id")] public required Guid Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("color")] public required string Color { get; init; }
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("fileCount")] public int FileCount { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Forma de la sesion.</summary>
public sealed record UsuarioDto
{
    [JsonPropertyName("id")] public required Guid Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("email")] public required string Email { get; init; }
    [JsonPropertyName("role")] public string Role { get; init; } = "user";
    [JsonPropertyName("avatarColor")] public string AvatarColor { get; init; } = "#6366f1";
}

/// <summary>Espacio usado, para el panel.</summary>
public sealed record EstadisticasDto
{
    [JsonPropertyName("totalFiles")] public int TotalFiles { get; init; }
    [JsonPropertyName("totalBytes")] public long TotalBytes { get; init; }
    [JsonPropertyName("totalFolders")] public int TotalFolders { get; init; }
    [JsonPropertyName("quotaBytes")] public long QuotaBytes { get; init; }
    [JsonPropertyName("warnBytes")] public long WarnBytes { get; init; }
    [JsonPropertyName("usedRatio")] public double UsedRatio { get; init; }

    [JsonPropertyName("byCategory")]
    public CategoriaEspacio[] ByCategory { get; init; } = [];
}

public sealed record CategoriaEspacio
{
    [JsonPropertyName("categoria")] public required string Categoria { get; init; }
    [JsonPropertyName("archivos")] public int Archivos { get; init; }
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
}

/// <summary>Respuesta de las rutas que devuelven una lista.</summary>
public sealed record ListaArchivosDto
{
    [JsonPropertyName("files")] public required ArchivoDto[] Files { get; init; }
}

public sealed record ListaCarpetasDto
{
    [JsonPropertyName("folders")] public required CarpetaDto[] Folders { get; init; }
}

public sealed record SesionDto
{
    [JsonPropertyName("user")] public UsuarioDto? User { get; init; }

    /// <summary>
    /// Si esta instalacion admite entrar con correo y contrasena.
    /// </summary>
    /// <remarks>
    /// El modo de acceso se decide en el servidor con NEXT_PUBLIC_AUTH_MODE, y
    /// viaja con la sesion para que la interfaz pueda enseñar solo lo que
    /// funciona. Sin esto, el boton de Google aparecia siempre y llevaba a una
    /// ruta que no existe cuando Google esta deshabilitado: el usuario veia que
    /// "Google esta roto" cuando en realidad estaba apagado a proposito.
    ///
    /// La comprobacion de verdad la sigue haciendo el servidor en cada ruta.
    /// Esto es solo para no ofrecer botones que no llevan a ningun sitio.
    /// </remarks>
    [JsonPropertyName("permiteLocal")] public bool PermiteLocal { get; init; } = true;

    /// <summary>Si esta instalacion admite entrar con Google.</summary>
    [JsonPropertyName("permiteGoogle")] public bool PermiteGoogle { get; init; }
}

/// <summary>Mensaje simple, que es lo que devuelve casi todo.</summary>
public sealed record MensajeDto
{
    [JsonPropertyName("message")] public required string Message { get; init; }
}

/// <summary>Respuesta de una subida correcta.</summary>
public sealed record SubidaDto
{
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("file")] public required ArchivoDto File { get; init; }
}
