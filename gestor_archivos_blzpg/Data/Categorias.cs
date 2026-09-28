namespace GestorArchivosBlzpg.Data;

/// <summary>
/// Categorias de archivo. Coinciden con el CHECK de la columna
/// <c>files.category</c>: anadir una aqui sin tocar la base haria fallar la
/// insercion.
/// </summary>
public static class Categorias
{
    public const string Pdf = "pdf";
    public const string Image = "image";
    public const string Word = "word";
    public const string Excel = "excel";
    public const string PowerPoint = "powerpoint";
    public const string Other = "other";


    /// <summary>
    /// Deduce la categoria del tipo MIME y de la extension.
    /// </summary>
    /// <remarks>
    /// Se mira el nombre ademas del MIME porque los navegadores no siempre
    /// mandan un tipo util: un .docx subido desde Windows llega como
    /// application/octet-stream, que no distingue de nada. La version con
    /// MongoDB hacia lo mismo, y por eso el orden importa: primero la extension
    /// para los formatos de oficina, despues el MIME.
    /// </remarks>
    public static string Deducir(string mimeType, string nombreArchivo)
    {
        var mime = (mimeType ?? "").ToLowerInvariant();
        var nombre = (nombreArchivo ?? "").ToLowerInvariant();

        if (mime == "application/pdf" || nombre.EndsWith(".pdf")) return Pdf;

        if (mime.StartsWith("image/") || nombre.EndsWith(".png") || nombre.EndsWith(".jpg")
            || nombre.EndsWith(".jpeg") || nombre.EndsWith(".webp") || nombre.EndsWith(".gif")
            || nombre.EndsWith(".svg"))
        {
            return Image;
        }

        if (mime == "application/msword"
            || mime == "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            || nombre.EndsWith(".docx") || nombre.EndsWith(".doc"))
        {
            return Word;
        }

        if (mime == "application/vnd.ms-powerpoint"
            || mime == "application/vnd.openxmlformats-officedocument.presentationml.presentation"
            || nombre.EndsWith(".pptx") || nombre.EndsWith(".ppt"))
        {
            return PowerPoint;
        }

        if (mime == "application/vnd.ms-excel"
            || mime == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            || nombre.EndsWith(".xlsx") || nombre.EndsWith(".xls"))
        {
            return Excel;
        }

        return Other;
    }
}
