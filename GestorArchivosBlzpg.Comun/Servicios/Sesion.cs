using Microsoft.AspNetCore.Components;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

using GestorArchivosBlzpg.Comun;

namespace GestorArchivosBlzpg.Comun;

/// <summary>
/// Sesion en el navegador.
/// </summary>
/// <remarks>
/// No guarda ningun token en el navegador. La cookie la pone el servidor, es
/// httpOnly y el navegador la manda sola en cada peticion. Aqui solo se guarda
/// el nombre y el correo, que es lo que se pinta en pantalla, y para saber si
/// hay sesion al arrancar.
/// </remarks>
public class Sesion(HttpClient http, NavigationManager nav)
{
    private UsuarioDto? _usuario;

    /// <summary>Usuario con la sesion iniciada, o null.</summary>
    public UsuarioDto? Usuario => _usuario;

    public bool Iniciada => _usuario is not null;

    /// <summary>
    /// Consulta al servidor quien es el usuario de esta sesion.
    /// </summary>
    /// <remarks>
    /// Se llama al arrancar y en cada recarga de pagina. Como la sesion vive en
    /// una cookie httpOnly, el codigo del navegador no puede leerla: la unica
    /// forma de saber si hay sesion es preguntando al servidor.
    /// </remarks>
    public async Task<UsuarioDto?> Consultar()
    {
        try
        {
            var respuesta = await http.GetFromJsonAsync<SesionDto>("/api/auth/me");

            // El 401 no es un error a propagar: significa que no hay sesion, que
            // es el caso normal de quien abre la aplicacion sin entrar.
            _usuario = respuesta?.User;
        }
        catch (HttpRequestException)
        {
            _usuario = null;
        }
        catch (JsonException)
        {
            _usuario = null;
        }

        return _usuario;
    }

    public async Task<(bool Ok, string Mensaje)> Entrar(string email, string contrasena)
    {
        var r = await http.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password = contrasena,
        });

        var cuerpo = await LeerMensaje(r);
        if (!r.IsSuccessStatusCode) return (false, cuerpo);

        await Consultar();
        return (true, cuerpo);
    }

    public async Task<(bool Ok, string Mensaje)> Registrarse(string nombre, string email, string contrasena)
    {
        var r = await http.PostAsJsonAsync("/api/auth/register", new
        {
            name = nombre,
            email,
            password = contrasena,
        });

        var cuerpo = await LeerMensaje(r);
        if (!r.IsSuccessStatusCode) return (false, cuerpo);

        await Consultar();
        return (true, cuerpo);
    }

    public async Task Salir()
    {
        await http.PostAsync("/api/auth/logout", null);
        _usuario = null;
        nav.NavigateTo("/auth");
    }

    /// <summary>
    /// Lee el campo "message" de la respuesta, o el codigo si no lo trae.
    /// </summary>
    /// <remarks>
    /// El servidor responde siempre con un mensaje pensado para la persona que
    /// lo lee, y se enseña tal cual. Si se perdiera, el usuario veria "Error"
    /// sin saber que hacer, cuando el motivo suele ser concreto ("ese correo ya
    /// tiene cuenta", "la contrasena no llega a 8 caracteres").
    /// </remarks>
    private static async Task<string> LeerMensaje(HttpResponseMessage r)
    {
        try
        {
            var cuerpo = await r.Content.ReadFromJsonAsync<MensajeDto>();
            if (!string.IsNullOrWhiteSpace(cuerpo?.Message)) return cuerpo.Message;
        }
        catch (Exception)
        {
            // Si el cuerpo no es el esperado, mejor un texto generico que fallar.
        }

        return r.IsSuccessStatusCode ? "Listo." : "No se ha podido completar la operacion.";
    }
}

/// <summary>
/// Proveedor de estado de autenticacion para los componentes.
/// </summary>
/// <remarks>
/// Blazor espera un <c>AuthenticationStateProvider</c> para saber si hay sesion.
/// Se envuelve el servicio de sesion en lugar de reimplementar la logica, y en
/// <c>IsAuthenticated</c> se comprueba que haya un usuario, no solo que la
/// peticion no haya fallado: son cosas distintas.
/// </remarks>
public class ProveedorAutenticacion(Sesion sesion) : AuthenticationStateProvider
{
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        => Construir(sesion.Usuario ?? await sesion.Consultar());

    /// <summary>
    /// Avisa a la interfaz de que la sesion ha cambiado.
    /// </summary>
    /// <remarks>
    /// Sin esto, un <c>AuthorizeView</c> que se haya resuelto antes de entrar o
    /// de salir se quedaria mostrando lo que ya no es cierto, porque Blazor solo
    /// vuelve a preguntar cuando se lo piden. Se llama al entrar y al salir, y
    /// se construye el estado con el usuario que hay ahora, no con uno vacio:
    /// un estado vacio aqui dejaria la interfaz sin sesion justo despues de
    /// haberla iniciado.
    /// </remarks>
    public void Notificar() => NotifyAuthenticationStateChanged(Task.FromResult(Construir(sesion.Usuario)));

    private static AuthenticationState Construir(UsuarioDto? usuario)
    {
        var identidad = usuario is null
            ? new System.Security.Claims.ClaimsIdentity()
            : new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.Name, usuario.Name),
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.Email, usuario.Email),
                    new System.Security.Claims.Claim(
                        System.Security.Claims.ClaimTypes.Role, usuario.Role),
                ],
                "cookie");

        return new AuthenticationState(new System.Security.Claims.ClaimsPrincipal(identidad));
    }
}
