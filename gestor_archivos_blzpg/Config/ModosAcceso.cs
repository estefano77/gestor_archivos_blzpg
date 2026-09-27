namespace GestorArchivosBlzpg.Config;

/// <summary>
/// Modos de acceso. Decide que formas de entrar estan disponibles, y se aplica
/// en el servidor: una ruta desactivada devuelve 404, no se limita a esconder el
/// boton.
/// </summary>
/// <remarks>
/// Mismo comportamiento que en <c>gestor_archivos_pg</c>, incluida la negativa a
/// adivinar. Si el valor no es 0, 1 ni 2, la aplicacion se niega a arrancar.
/// Elegir "ambas" por defecto cuando alguien queria restringir dejaria
/// abierto un metodo que creia cerrado, y eso no se descubre hasta que alguien
/// lo usa.
/// </remarks>
public enum ModoAcceso
{
    /// <summary>Solo correo y contrasena.</summary>
    SoloLocal = 0,

    /// <summary>Solo Google.</summary>
    SoloGoogle = 1,

    /// <summary>Los dos.</summary>
    Ambos = 2,
}

public static class ModosAcceso
{
    public const string Clave = "NEXT_PUBLIC_AUTH_MODE";

    /// <summary>
    /// Lee el modo de la configuracion. Lanza si el valor no es valido, en lugar
    /// de devolver un valor por defecto.
    /// </summary>
    public static ModoAcceso Leer(IConfiguration config)
    {
        var bruto = config[Clave];

        // Sin definir, se asume el modo mas restrictivo.
        if (string.IsNullOrWhiteSpace(bruto)) return ModoAcceso.SoloLocal;

        if (!int.TryParse(bruto, out var n) || !Enum.IsDefined(typeof(ModoAcceso), n))
        {
            throw new InvalidOperationException(
                $"{Clave}={bruto} no es un modo valido. Usa 0 (solo correo y " +
                "contrasena), 1 (solo Google) o 2 (ambos). La aplicacion no " +
                "adivina, porque elegir por su cuenta podria dejar abierto un " +
                "metodo de acceso que se pretendia cerrar."
            );
        }

        return (ModoAcceso)n;
    }

    public static bool PermiteLocal(this ModoAcceso m) =>
        m is ModoAcceso.SoloLocal or ModoAcceso.Ambos;

    public static bool PermiteGoogle(this ModoAcceso m) =>
        m is ModoAcceso.SoloGoogle or ModoAcceso.Ambos;

    /// <summary>Texto del modo, para los diagnosticos de arranque.</summary>
    public static string Texto(this ModoAcceso m) => m switch
    {
        ModoAcceso.SoloLocal => "solo correo y contrasena",
        ModoAcceso.SoloGoogle => "solo Google",
        _ => "correo y contrasena, y Google",
    };
}
