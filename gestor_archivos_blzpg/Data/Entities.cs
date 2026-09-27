namespace GestorArchivosBlzpg.Data;

/// <summary>
/// Entidad de <c>users</c>. Mapea la tabla que ya existe en PostgreSQL; el
/// esquema es el del proyecto <c>gestor_archivos_pg</c> y no lo cambia esta
/// aplicacion.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    /// <summary>
    /// Hash bcrypt, o null si la cuenta viene de Google. Se deja en string y no
    /// en byte[] porque en PostgreSQL la columna es <c>text</c>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>"local" o "google". Coincide con el CHECK de la base.</summary>
    public string Provider { get; set; } = "local";

    /// <summary>Identificador del proveedor (<c>sub</c> de Google). Null en cuentas locales.</summary>
    public string? ProviderId { get; set; }

    public string AvatarColor { get; set; } = "#6366f1";

    /// <summary>"user" o "admin". Coincide con el CHECK de la base.</summary>
    public string Role { get; set; } = "user";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<FileItem> Files { get; set; } = new List<FileItem>();
}

/// <summary>
/// Entidad de <c>folders</c>. Las carpetas no se anidan: la tabla tiene un
/// <c>user_id</c> y ningun <c>parent_id</c>.
/// </summary>
public class Folder
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Color { get; set; } = "indigo";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public ICollection<FileItem> Files { get; set; } = new List<FileItem>();
}

/// <summary>
/// Entidad de <c>files</c>. La columna <c>folder_id</c> es una clave foranea
/// real a <c>folders.id</c>, a diferencia de la version con MongoDB, donde cada
/// fila guardaba el nombre de la carpeta.
/// </summary>
public class FileItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Carpeta que contiene el archivo, o null si esta sin carpeta.</summary>
    public Guid? FolderId { get; set; }

    public string OriginalName { get; set; } = "";
    public string MimeType { get; set; } = "";

    /// <summary>pdf, image, word, excel, powerpoint u other. Coincide con el CHECK.</summary>
    public string Category { get; set; } = "other";

    public long Size { get; set; }

    /// <summary>
    /// Donde vive el binario, no el binario. Es la razon de que la base no
    /// guarde archivos: aqui va la clave del objeto en el almacen.
    /// </summary>
    public string StoragePath { get; set; } = "";

    public string Description { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public User? User { get; set; }
    public Folder? Folder { get; set; }
}
