using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BCrypt.Net;
using GestorArchivosBlzpg.Comun;
using GestorArchivosBlzpg.Config;
using GestorArchivosBlzpg.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace GestorArchivosBlzpg.Api;

/// <summary>Peticion de registro o de acceso con correo.</summary>
public sealed record Credenciales(
    [property: Required] string Email,
    [property: Required] string Password,
    string? Name);

/// <summary>
/// Rutas de sesion con correo y contrasena.
/// </summary>
public static class RutasAuth
{
    /// <summary>
    /// Crea una cuenta.
    /// </summary>
    /// <remarks>
    /// Comprueba la contrasena con una longitud minima, pero no impone reglas
    /// de composicion (una mayuscula, un simbolo...). Componer reglas asi
    /// empuja a la gente a escribir contrasenas mas debiles, porque el
    /// unico camino que cumple la regla es "Anotalo2024!". La longitud minima
    /// si aporta algo.
    /// </remarks>
    public static async Task<IResult> Registrar(
        Credenciales body,
        AppDbContext db,
        ModoAccesoActual modo,
        HttpContext http)
    {
        if (!modo.PermiteLocal)
        {
            return Results.Json(
                new { message = "El registro esta deshabilitado en esta instalacion." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!ModelStateValido(body)) return Errores("Revisa los datos.");

        var email = body.Email.Trim().ToLowerInvariant();

        if (!ValidarEmail(email))
        {
            return Respuestas.Error("El correo no parece valido.", StatusCodes.Status400BadRequest);
        }

        if ((body.Password?.Length ?? 0) < 8)
        {
            return Respuestas.Error(
                "La contrasena debe tener al menos 8 caracteres.",
                StatusCodes.Status400BadRequest);
        }

        // Unicidad con comparacion insensible a mayusculas, hecha aqui y no
        // solo en la base. El indice unico de PostgreSQL es sobre el texto tal
        // cual, asi que sin esta comprobacion "Juan@x.com" y "JUAN@x.com"
        // entrarian las dos.
        var yaExiste = await db.Users.AnyAsync(u => u.Email.ToLower() == email);
        if (yaExiste)
        {
            return Respuestas.Error("Ya hay una cuenta con ese correo.", StatusCodes.Status409Conflict);
        }

        var nombre = string.IsNullOrWhiteSpace(body.Name) ? email.Split('@')[0] : body.Name.Trim();

        if (nombre.Length > 60)
        {
            return Respuestas.Error("El nombre no puede pasar de 60 caracteres.", StatusCodes.Status400BadRequest);
        }

        var usuario = new User
        {
            Name = nombre,
            Email = email,
            // bcrypt con coste 10, el mismo que usaba la version anterior, para
            // que los hashes ya guardados sigan verificandose.
            Password = BCrypt.Net.BCrypt.HashPassword(body.Password, workFactor: 10),
            Provider = "local",
            AvatarColor = ColorAleatorio(),
        };

        db.Users.Add(usuario);
        await db.SaveChangesAsync();

        await Entrar(http, usuario);
        return Results.Json(
            new { message = "Cuenta creada", user = Dto.Usuario(usuario) },
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Inicia sesion con correo y contrasena.
    /// </summary>
    public static async Task<IResult> Login(
        Credenciales body,
        AppDbContext db,
        ModoAccesoActual modo,
        HttpContext http)
    {
        if (!modo.PermiteLocal)
        {
            return Results.Json(
                new { message = "El acceso con correo esta deshabilitado en esta instalacion." },
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (!ModelStateValido(body)) return Errores("Revisa los datos.");

        var email = body.Email.Trim().ToLowerInvariant();
        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        // Mismo mensaje y mismo codigo tanto si el correo no existe como si la
        // contrasena no es la correcta. Distinguirlos permitiria saber que
        // correos estan registrados en el sistema.
        if (usuario is null || string.IsNullOrEmpty(usuario.Password))
        {
            return CredencialesIncorrectas();
        }

        bool correcta;
        try
        {
            correcta = BCrypt.Net.BCrypt.Verify(body.Password, usuario.Password);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // El hash guardado no es bcrypt valido. Pasa si alguien toco la
            // base a mano. Se trata como contrasena incorrecta, que es lo que
            // el usuario puede entender.
            correcta = false;
        }

        if (!correcta) return CredencialesIncorrectas();

        await Entrar(http, usuario);
        return Results.Json(new { message = "Inicio de sesion exitoso", user = Dto.Usuario(usuario) });
    }

    /// <summary>Cierra la sesion borrando la cookie.</summary>
    public static async Task<IResult> Logout(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Json(new { message = "Sesion cerrada" });
    }

    /// <summary>
    /// Devuelve la sesion actual. Es la primera llamada que hace el cliente, y
    /// sirve para decidir si va a /auth o al panel.
    /// </summary>
    public static async Task<IResult> Yo(HttpContext http, AppDbContext db, ModoAccesoActual modo)
    {
        // El modo de acceso acompaña siempre a la respuesta, tambien cuando no
        // hay sesion: la pantalla de acceso lo necesita para saber si enseñar el
        // boton de Google o el formulario, y es justo el momento en que no hay
        // usuario que devolver.
        var sesion = new SesionDto
        {
            PermiteLocal = modo.PermiteLocal,
            PermiteGoogle = modo.PermiteGoogle,
        };

        var id = UsuarioActual.Id(http.User);
        if (id is null)
        {
            return Results.Json(sesion with { User = null });
        }

        var usuario = await db.Users.FirstOrDefaultAsync(u => u.Id == id.Value);
        if (usuario is null)
        {
            // La cookie es valida pero el usuario ya no esta. Puede pasar si se
            // borro la fila desde la base. Se cierra la sesion en vez de dejar
            // al cliente creerse que ha entrado.
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Json(sesion with { User = null });
        }

        return Results.Json(sesion with { User = Dto.Usuario(usuario) });
    }

    /// <summary>
    /// Escribe la cookie de sesion.
    /// </summary>
    /// <remarks>
    /// Se centraliza aqui porque Google entra por su propio camino y necesita
    /// exactamente la misma cookie. Si cada uno montase la suya, una cuenta
    /// acabaria con dos formas de estar dentro y solo se podria cerrar una.
    /// </remarks>
    public static async Task Entrar(HttpContext http, User usuario)
    {
        var identidad = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
                new Claim(ClaimTypes.Name, usuario.Name),
                new Claim(ClaimTypes.Email, usuario.Email),
                new Claim(ClaimTypes.Role, usuario.Role),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identidad),
            new AuthenticationProperties { IsPersistent = true });
    }

    private static IResult CredencialesIncorrectas() => Results.Json(
        new { message = "Correo o contrasena incorrectos." },
        statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Errores(string mensaje) => Results.Json(
        new { message = mensaje },
        statusCode: StatusCodes.Status400BadRequest);

    private static bool ModelStateValido(Credenciales c)
        => !string.IsNullOrWhiteSpace(c.Email) && !string.IsNullOrWhiteSpace(c.Password);

    /// <summary>
    /// Valida el formato del correo sin ser demasiado estricto.
    /// </summary>
    /// <remarks>
    /// Se limita a comprobar que hay algo antes y despues de una arroba, sin
    /// exigir puntos en el dominio ni tipos conocidos. Un validador que acepta
    /// "a@b" rechaza correo valido de una intranet; uno que acepta casi todo
    /// deja pasar basura, que es un problema mucho menor.
    /// </remarks>
    private static bool ValidarEmail(string email)
    {
        var i = email.IndexOf('@');
        return i > 0 && i < email.Length - 1 && email.IndexOf('@', i + 1) == -1;
    }

    /// <summary>Un color estable de la paleta, elegido a partir del identificador.</summary>
    private static string ColorAleatorio()
    {
        string[] colores = ["#6366f1", "#0ea5e9", "#10b981", "#f59e0b", "#f43f5e", "#a855f7"];
        return colores[Random.Shared.Next(colores.Length)];
    }
}
