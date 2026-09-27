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

    /// <summary>Prefijo de todo lo que pertenece a un usuario.</summary>
    public static string PrefijoUsuario(Guid userId) => $"{userId}/";

    /// <summary>
    /// Prefijo de los binarios de una carpeta concreta.
    /// </summary>
    /// <remarks>
    /// Las carpetas son logicas: viven en la tabla <c>folders</c> y no tienen
    /// carpeta fisica en el almacen. Por eso el prefijo de una carpeta se
    /// construye con el identificador de la carpeta y no con su nombre, que
    /// ademas puede cambiar.
    /// </remarks>
    public static string PrefijoCarpeta(Guid userId, Guid carpetaId)
        => $"{userId}/{carpetaId:N}/";
}

/// <summary>Motivos por los que el servidor devuelve 413.</summary>
public static class MotivosRechazo
{
    public const string Tamano = "tamano";
    public const string Cuota = "cuota";
}
