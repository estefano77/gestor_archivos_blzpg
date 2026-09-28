namespace GestorArchivosBlzpg.Config;

/// <summary>
/// Limites y reglas de negocio que se muestran en pantalla.
/// </summary>
/// <remarks>
/// Los valores salen de la configuracion y llevan los mismos numeros que la
/// version <c>gestor_archivos_pg</c>, para que el manual de usuario y las
/// capturas sirvan para las dos aplicaciones sin distinguirlas.
/// </remarks>
public static class AppLimits
{
    /// <summary>Tope por archivo. Lo impone la aplicacion, no la base.</summary>
    public const string ClaveMaxArchivo = "LIMITE_MB_ARCHIVO";

    /// <summary>Cuota total por cuenta.</summary>
    public const string ClaveCuenta = "LIMITE_MB_CUENTA";

    /// <summary>Fraccion de la cuota a partir de la cual se avisa.</summary>
    public const string ClaveAviso = "AVISO_CUOTA";

    public static long MaxFileSize(Configuracion c) => c.Obtener(ClaveMaxArchivo, 15L) * 1024 * 1024;

    public static long UserQuotaBytes(Configuracion c) => c.Obtener(ClaveCuenta, 25L) * 1024 * 1024;

    public static double QuotaWarnRatio(Configuracion c) => c.Obtener(ClaveAviso, 0.8);

    public static double QuotaWarnPercent(Configuracion c) => QuotaWarnRatio(c) * 100;

}
