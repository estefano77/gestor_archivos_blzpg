using System.Security.Cryptography;
using System.Text;
using GestorArchivosBlzpg.Config;
using GestorArchivosBlzpg.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestorArchivosBlzpg.Api;

/// <summary>
/// Inicio de sesion con Google: las dos rutas del flujo OAuth.
/// </summary>
public static class RutasGoogle
{
    /// <summary>Cookie temporal que empareja el state con quien lo genero.</summary>
    public const string CookieState = "google_oauth_state";

    /// <summary>Lo que vive el state. Suficiente para el viaje de ida y vuelta.</summary>
    private const int SegundosState = 600;

    /// <summary>
    /// Inicio del flujo: genera un state, lo guarda en una cookie y lleva a la
    /// pantalla de consentimiento de Google.
    /// </summary>
    /// <remarks>
    /// El <c>state</c> es lo que impide que alguien enganche a esta sesion una
    /// respuesta de Google ajena. Sin el, un tercero podria hacer que la cuenta
    /// de otra persona acabase vinculada a la sesion de quien visita su pagina.
    /// </remarks>
    public static IResult Iniciar(
        HttpContext http, ServicioGoogle google, ModoAccesoActual modo)
    {
        // El modo 0 deja Google fuera. La ruta se cierra igual que el boton, para
        // que no se pueda saltar el ajuste llamando a la direccion a mano.
        if (!modo.PermiteGoogle)
        {
            return Respuestas.Error(
                "El inicio de sesion con Google esta deshabilitado en esta instalacion.",
                StatusCodes.Status403Forbidden);
        }

        // Sin credenciales no se ofrece el boton, asi que este camino solo se
        // alcanza si alguien escribe la direccion a mano.
        if (!google.Configurado)
        {
            return Respuestas.Error(
                "El inicio de sesion con Google no esta configurado.",
                StatusCodes.Status503ServiceUnavailable);
        }

        string redirectUri;
        try
        {
            redirectUri = google.ResolverRedirectUri(http.Request);
        }
        catch (ErrorGoogle ex)
        {
            return Respuestas.Error(ex.Message, StatusCodes.Status500InternalServerError);
        }

        var state = GenerarState();

        http.Response.Cookies.Append(CookieState, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromSeconds(SegundosState),
            Path = "/",
        });

        return Results.Redirect(google.ConstruirUrlAutorizacion(redirectUri, state));
    }

    /// <summary>
    /// Vuelta de Google: verifica la respuesta, localiza o crea la cuenta y
    /// abre la sesion.
    /// </summary>
    public static async Task<IResult> Callback(
        HttpContext http,
        ServicioGoogle google,
        ModoAccesoActual modo,
        AppDbContext db,
        CancellationToken ct)
    {
        // Una vuelta pendiente de una pestaña ya abierta no debe poder completar
        // el inicio de sesion si el administrador paso a un modo sin Google.
        if (!modo.PermiteGoogle)
        {
            return Respuestas.Error(
                "El inicio de sesion con Google esta deshabilitado en esta instalacion.",
                StatusCodes.Status403Forbidden);
        }

        if (!google.Configurado)
        {
            return Respuestas.Error(
                "El inicio de sesion con Google no esta configurado.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var parametros = http.Request.Query;

        // La persona pulso "Cancelar" en la pantalla de Google.
        if (!string.IsNullOrEmpty(parametros["error"]))
        {
            return VolverConError(http, "Has cancelado el inicio de sesion con Google.");
        }

        var code = parametros["code"].ToString();
        var state = parametros["state"].ToString();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        {
            return VolverConError(http, "Google no devolvio una respuesta valida.");
        }

        // --- Proteccion CSRF: el state debe coincidir con el de nuestra cookie ---
        var esperado = http.Request.Cookies[CookieState];
        if (string.IsNullOrEmpty(esperado) || !IgualesEnTiempoConstante(state, esperado))
        {
            return VolverConError(http, "No se pudo verificar el inicio de sesion. Vuelve a intentarlo.");
        }

        try
        {
            // Debe ser la misma URI que se mando en la peticion de autorizacion.
            // Si difieren aunque sea en un caracter, Google rechaza el canje.
            var redirectUri = google.ResolverRedirectUri(http.Request);

            var idToken = await google.CanjearCodigo(code, redirectUri, ct);
            var identidad = await google.VerificarIdToken(idToken, ct);

            var usuario = await LocalizarOCrear(db, identidad, ct);

            await RutasAuth.Entrar(http, usuario);

            http.Response.Cookies.Delete(CookieState);
            return Results.Redirect("/");
        }
        catch (ErrorGoogle ex)
        {
            return VolverConError(http, ex.Message);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error en el inicio de sesion con Google: {ex}");
            return VolverConError(http, "Ha ocurrido un error al iniciar sesion con Google.");
        }
    }

    /// <summary>
    /// Localiza la cuenta de Google o la crea.
    /// </summary>
    /// <remarks>
    /// Se busca primero por el identificador de Google (<c>sub</c>), que es
    /// estable aunque la persona cambie de correo. Si no aparece, se busca por
    /// correo: eso convierte una cuenta creada con contrasena en una cuenta con
    /// Google, en vez de duplicarla y repartir los archivos entre dos
    /// identidades. Es seguro precisamente porque el correo esta verificado por
    /// Google, que es lo que se comprobo al verificar el token.
    /// </remarks>
    private static async Task<User> LocalizarOCrear(
        AppDbContext db, IdentidadGoogle identidad, CancellationToken ct)
    {
        var porIdDeGoogle = await db.Users
            .FirstOrDefaultAsync(u => u.Provider == "google" && u.ProviderId == identidad.GoogleId, ct);

        if (porIdDeGoogle is not null) return porIdDeGoogle;

        var porCorreo = await db.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == identidad.Email, ct);

        if (porCorreo is not null)
        {
            // Se entra en la cuenta que ya existia, con sus archivos. Solo se
            // anaden provider y provider_id; lo demas no se toca, y en particular
            // la contrasena se deja, para que siga pudiendo entrar por las dos vias.
            porCorreo.Provider = "google";
            porCorreo.ProviderId = identidad.GoogleId;
            await db.SaveChangesAsync(ct);
            return porCorreo;
        }

        var nueva = new User
        {
            Name = identidad.Nombre,
            Email = identidad.Email,
            Provider = "google",
            ProviderId = identidad.GoogleId,
            AvatarColor = ColorAleatorio(),

            // Sin contrasena: esta cuenta no tiene y no debe tener. Dejarla en
            // null es lo que hace que el login por contrasena la rechace en vez
            // de comparar contra un hash inventado.
            Password = null,
        };

        db.Users.Add(nueva);

        try
        {
            await db.SaveChangesAsync(ct);
            return nueva;
        }
        catch (DbUpdateException ex) when (EsViolacionDeUnicidad(ex))
        {
            // Dos inicios de sesion simultaneos con el mismo correo pueden chocar
            // con el indice unico. Quien haya ganado la carrera ya creo la cuenta,
            // asi que se usa esa en lugar de fallar.
            var recienCreada = await db.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == identidad.Email, ct);

            if (recienCreada is not null) return recienCreada;
            throw;
        }
    }

    /// <summary>Si la excepcion es una violacion de unicidad de PostgreSQL.</summary>
    private static bool EsViolacionDeUnicidad(DbUpdateException ex)
        => ex.InnerException is PostgresException pg && pg.SqlState == "23505";

    /// <summary>Vuelve a la pantalla de acceso con un mensaje legible.</summary>
    private static IResult VolverConError(HttpContext http, string mensaje)
    {
        http.Response.Cookies.Delete(CookieState);
        return Results.Redirect($"/auth?error={Uri.EscapeDataString(mensaje)}");
    }

    /// <summary>
    /// Compara dos cadenas sin que el tiempo que tarda dependa de donde difieren.
    /// </summary>
    /// <remarks>
    /// Una comparacion normal termina en el primer caracter distinto, asi que el
    /// tiempo revela cuantos caracteres iniciales coinciden. A fuerza de intentos,
    /// eso permite adivinar el state. <c>FixedTimeEquals</c> compara siempre los
    /// mismos bytes, coincidan o no.
    /// </remarks>
    private static bool IgualesEnTiempoConstante(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>State aleatorio de 32 bytes, en base64url.</summary>
    private static string GenerarState()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static string ColorAleatorio()
    {
        string[] colores = ["#6366f1", "#0ea5e9", "#10b981", "#f59e0b", "#f43f5e", "#a855f7"];
        return colores[Random.Shared.Next(colores.Length)];
    }
}
