using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GestorArchivosBlzpg.Config;

/// <summary>
/// Identidad que devuelve Google, ya verificada.
/// </summary>
/// <param name="GoogleId">Claim <c>sub</c>: identificador estable en Google.</param>
/// <param name="Email">Correo, en minusculas y sin espacios.</param>
/// <param name="Nombre">Nombre, recortado al maximo que admite el esquema.</param>
public sealed record IdentidadGoogle(string GoogleId, string Email, string Nombre);

/// <summary>
/// Fallo de Google que se le puede enseñar a la persona.
/// </summary>
/// <remarks>
/// Se distingue del resto de excepciones para que el mensaje que ve el usuario
/// sea el especifico. Un error inesperado se registra y se traduce a un texto
/// generico, porque su detalle no le sirve a nadie que no sea quien programa.
/// </remarks>
public sealed class ErrorGoogle(string mensaje) : Exception(mensaje);

/// <summary>
/// Verificacion de la identidad que devuelve Google.
/// </summary>
/// <remarks>
/// Google devuelve un <c>id_token</c>, que es un JWT firmado por Google. Se
/// verifica contra las claves publicas que Google publica: no hay secreto
/// compartido para esto, y por eso no hace falta guardar ninguna clave.
///
/// Solo se usa el <c>client_secret</c> para canjear el codigo por el token. La
/// verificacion de la firma no lo necesita.
/// </remarks>
public class ServicioGoogle(IOptions<OpcionesGoogle> opciones, IHttpClientFactory http)
{
    private readonly OpcionesGoogle o = opciones.Value;

    /// <summary>
    /// Si hay credenciales de Google configuradas.
    /// </summary>
    /// <remarks>
    /// Se expone aqui y no se pasa la configuracion a las rutas. Un parametro de
    /// tipo OpcionesGoogle en la firma de un handler de Minimal API se interpreta
    /// como cuerpo de la peticion, y el error que produce al arrancar ("Body was
    /// inferred but the method does not allow inferred body parameters") no
    /// menciona ni el tipo ni la ruta.
    /// </remarks>
    public bool Configurado => o.Configurado;

    /// <summary>Ruta de la callback. Es lo que se registra en Google Cloud.</summary>
    public const string RutaCallback = "/api/auth/google/callback";

    /// <summary>
    /// Google usa <c>accounts.google.com</c> como emisor, pero historicamente
    /// tambien ha emitido tokens sin el esquema. Se aceptan los dos para no
    /// rechazar cuentas validas.
    /// </summary>
    private static readonly string[] Emisores =
        ["https://accounts.google.com", "accounts.google.com"];

    /// <summary>
    /// Documento de configuracion de Google, con cache.
    /// </summary>
    /// <remarks>
    /// <see cref="ConfigurationManager{T}"/> descarga las claves publicas y las
    /// renueva cuando Google rota. Hacerlo a mano en cada peticion seria un viaje
    /// de red por inicio de sesion, y guardarlas sin caducidad romperia el dia
    /// que Google rote, que es justo el fallo que mas tarda en notarse.
    /// </remarks>
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> Configuracion =
        new(
            "https://accounts.google.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });

    /// <summary>
    /// Resuelve la URI de redireccion que hay que mandar a Google.
    /// </summary>
    /// <remarks>
    /// Se valida aqui, al usarla, para que un error de tecleo salga como un
    /// mensaje claro en vez de como un <c>redirect_uri_mismatch</c> que Google
    /// devuelve veinte minutos despues y sin decir que campo esta mal.
    /// </remarks>
    public string ResolverRedirectUri(HttpRequest peticion)
    {
        var configurada = o.RedirectUri?.Trim();
        if (string.IsNullOrWhiteSpace(configurada))
        {
            return $"{peticion.Scheme}://{peticion.Host}{RutaCallback}";
        }

        if (!Uri.TryCreate(configurada, UriKind.Absolute, out var uri))
        {
            throw new ErrorGoogle(
                $"GOOGLE_REDIRECT_URI no es una direccion valida: \"{configurada}\". " +
                "Debe ser algo como https://tu-dominio.com/api/auth/google/callback");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ErrorGoogle(
                $"GOOGLE_REDIRECT_URI debe empezar por http:// o https://, pero es \"{configurada}\".");
        }

        return uri.ToString();
    }

    /// <summary>
    /// Construye la direccion de la pantalla de consentimiento.
    /// </summary>
    /// <remarks>
    /// Los permisos <c>openid email profile</c> no son sensibles: no pasan por
    /// la revision de Google, a diferencia de <c>drive</c> o <c>gmail</c>.
    ///
    /// <c>prompt=select_account</c> obliga a elegir cuenta cada vez. Sin eso,
    /// Google entra directo con la que tenga la sesion abierta, y quien use dos
    /// cuentas no tiene forma de elegir la otra.
    /// </remarks>
    public string ConstruirUrlAutorizacion(string redirectUri, string state)
    {
        var parametros = new Dictionary<string, string?>
        {
            ["client_id"] = o.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email profile",
            ["state"] = state,
            ["prompt"] = "select_account",
        };

        var consulta = string.Join("&", parametros
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}"));

        return $"https://accounts.google.com/o/oauth2/v2/auth?{consulta}";
    }

    /// <summary>
    /// Canjea el codigo de autorizacion por el token de identidad.
    /// </summary>
    /// <remarks>
    /// El secreto va en el cuerpo del formulario, que es lo que espera
    /// <c>client_secret_post</c>, el metodo que usa este cliente en la consola
    /// de Google. Mandarlo en la cabecera (<c>client_secret_basic</c>) tambien
    /// funciona si ahi esta declarado, pero no si no lo esta.
    /// </remarks>
    public async Task<string> CanjearCodigo(string code, string redirectUri, CancellationToken ct = default)
    {
        var cuerpo = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = o.ClientId,
            ["client_secret"] = o.ClientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
        };

        using var respuesta = await http.CreateClient("google")
            .PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(cuerpo), ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            // El cuerpo de error de Google dice cual de los cuatro parametros
            // esta mal. Se registra, porque sin el este fallo es adivinanza.
            var detalle = await respuesta.Content.ReadAsStringAsync(ct);
            Console.Error.WriteLine($"Google rechazo el canje del codigo: {(int)respuesta.StatusCode} {detalle}");

            throw new ErrorGoogle("Google no ha aceptado el inicio de sesion. Vuelve a intentarlo.");
        }

        var datos = await respuesta.Content.ReadFromJsonAsync<RespuestaTokens>(ct);
        if (string.IsNullOrWhiteSpace(datos?.IdToken))
        {
            throw new ErrorGoogle("Google no devolvio un token de identidad.");
        }

        return datos.IdToken;
    }

    /// <summary>
    /// Verifica el token de identidad y devuelve la cuenta.
    /// </summary>
    /// <remarks>
    /// Rechaza si la firma no es de Google, si el token es de otra aplicacion
    /// (audiencia distinta), si ha caducado, o si Google no confirma que el
    /// correo este verificado.
    ///
    /// Esta ultima comprobacion es la que sostiene todo lo demas. Sin ella,
    /// alguien podria registrar una direccion de correo que no posee y, como el
    /// codigo vincula por correo las cuentas ya existentes, se quedaria con la
    /// cuenta del titular y con sus archivos.
    /// </remarks>
    public async Task<IdentidadGoogle> VerificarIdToken(string idToken, CancellationToken ct = default)
    {
        var configuracion = await Configuracion.GetConfigurationAsync(ct);

        var parametros = new TokenValidationParameters
        {
            ValidIssuers = Emisores,
            ValidAudience = o.ClientId,
            IssuerSigningKeys = configuracion.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            // Cinco minutos de margen por desfase de reloj. Sin esto, un equipo
            // con la hora unos segundos adelantada rechaza tokens recien
            // emitidos, y el mensaje que sale es "el token ha caducado", que
            // apunta al sitio equivocado.
            ClockSkew = TimeSpan.FromMinutes(5),
        };

        JwtSecurityToken token;
        try
        {
            new JwtSecurityTokenHandler()
                .ValidateToken(idToken, parametros, out var validado);
            token = (JwtSecurityToken)validado;
        }
        catch (Exception ex)
        {
            // Se registra el motivo (firma, audiencia, caducidad) porque sin el
            // el fallo es imposible de diagnosticar. Nunca se registra el token.
            Console.Error.WriteLine($"Fallo al verificar el id_token de Google: {ex.Message}");
            throw new ErrorGoogle("El token de Google no es valido o ha caducado. Vuelve a intentarlo.");
        }

        // Se leen los claims por su nombre original. El manejador de JWT renombra
        // sub, email y name a los nombres largos de .NET si se le deja, y a partir
        // de ahi hay que adivinar como se llama cada uno.
        var sub = token.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var email = token.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        var verificado = token.Claims.FirstOrDefault(c => c.Type == "email_verified")?.Value;
        var nombre = token.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

        if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(email))
        {
            throw new ErrorGoogle("Google no devolvio los datos de la cuenta.");
        }

        if (!string.Equals(verificado, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new ErrorGoogle(
                "Google no confirma que este correo este verificado. Usa una cuenta " +
                "de Google con el correo confirmado.");
        }

        var correoLimpio = email.ToLowerInvariant().Trim();

        // El nombre que da Google puede pasar de 60 caracteres, que es el maximo
        // del esquema. Se recorta aqui para que la insercion no falle con una
        // violacion del CHECK, que no diria nada de donde viene el problema.
        var nombreLimpio = string.IsNullOrWhiteSpace(nombre)
            ? correoLimpio.Split('@')[0]
            : nombre.Trim();

        return new IdentidadGoogle(sub, correoLimpio, nombreLimpio[..Math.Min(60, nombreLimpio.Length)]);
    }

    /// <summary>Respuesta del endpoint de tokens de Google.</summary>
    private sealed record RespuestaTokens
    {
        [JsonPropertyName("id_token")] public string? IdToken { get; init; }
    }
}
