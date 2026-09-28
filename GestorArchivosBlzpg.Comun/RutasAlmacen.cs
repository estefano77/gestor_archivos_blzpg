namespace GestorArchivosBlzpg.Comun;

/// <summary>Como se guarda un archivo en el almacen.</summary>
public static class RutasAlmacen
{
    /// <summary>
    /// Ruta del binario de un archivo, tal como la guarda el servidor.
    /// </summary>
    /// <remarks>
    /// La construye el servidor con el identificador del usuario y un GUID, y
    /// la termina con el nombre original para que se pueda reconocer al
    /// mirar el almacen. Nunca se acepta desde el cliente: si se aceptara, un
    /// nombre con ".." podria escribir fuera de la carpeta del usuario.
    ///
    /// El GUID intermedio hace que dos archivos con el mismo nombre convivan,
    /// que es justo lo que permite el renombrado sin tener que mover el
    /// binario.
    /// </remarks>
    public static string Para(Guid userId, Guid archivoId, string nombreOriginal)
        => $"{userId}/{archivoId:N}/{nombreOriginal}";
}

