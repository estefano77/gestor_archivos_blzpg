namespace GestorArchivosBlzpg.Config;

/// <summary>
/// Lectura de la configuracion con valor por defecto.
/// </summary>
/// <remarks>
/// Se usa en lugar de leer <c>Environment.GetEnvironmentVariable</c> a pelo
/// porque asi los valores llegan por los mismos canales que el resto: appsettings,
/// variables de entorno y <c>user-secrets</c>. Con leitura directa, un secreto
/// puesto en un archivo de la maquina no se veria.
/// </remarks>
public class Configuracion(IConfiguration config)
{
    public string Obtener(string clave, string porDefecto)
    {
        var v = config[clave];
        return string.IsNullOrWhiteSpace(v) ? porDefecto : v;
    }

    public long Obtener(string clave, long porDefecto)
    {
        var v = config[clave];

        // Un limite mal escrito no debe impedir que la aplicacion arranque: el
        // valor por defecto es el correcto, y avisar es mejor que fallar.
        if (long.TryParse(v, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        if (!string.IsNullOrWhiteSpace(v))
        {
            Console.Error.WriteLine($"Aviso: {clave}={v} no es un numero; se usa {porDefecto}.");
        }

        return porDefecto;
    }

    public double Obtener(string clave, double porDefecto)
    {
        var v = config[clave];

        // Invariant a proposito: la configuracion se escribe con punto decimal y
        // en un equipo en espanol "0,8" no debe leerse como ocho décimas.
        if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return porDefecto;
    }
}
