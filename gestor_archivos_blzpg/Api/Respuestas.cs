using System.Security.Claims;
using GestorArchivosBlzpg.Data;

namespace GestorArchivosBlzpg.Api;

/// <summary>
/// Identidad de quien hace la peticion, deducida del cookie firmado.
/// </summary>
/// <remarks>
/// La API no acepta nunca un identificador de usuario vindo en el cuerpo o en
/// la consulta: siempre se lee de la cookie, que solo el servidor puede firmar.
/// Por eso cualquiera que llame a la API con curl recibe lo suyo y no lo de
/// otro, sin que haya que comprobar nada mas.
/// </remarks>
public static class UsuarioActual
{
    /// <summary>
    /// Identificador del usuario de la peticion, o null si no hay sesion.
    /// </summary>
    /// <remarks>
    /// Se busca por el tipo de claim que emite <c>ClaimTypes.NameIdentifier</c>.
    /// Name, no "sub": el nombre de la claim lo pone el marco, y hardcodearlo
    /// funciona hoy y falla en cuanto se cambia la configuracion de los claims.
    /// </remarks>
    public static Guid? Id(ClaimsPrincipal? principal)
    {
        var claim = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    public static bool EsAdmin(ClaimsPrincipal? principal)
        => principal?.IsInRole("admin") == true;
}

/// <summary>
/// Extensiones para responder con los mismos codigos y mensajes que la version
/// anterior, para que el manual de usuario siga describiendo la misma cosa.
/// </summary>
public static class Respuestas
{
    /// <summary>
    /// Devuelve 401 si no hay sesion. Se usa al principio de cada ruta
    /// protegida: es preferible un unico <c>return</c> a repetir la
    /// comprobacion por endpoint y que se olvide en alguno.
    /// </summary>
    public static IResult? SinSesion(ClaimsPrincipal principal, out Guid userId)
    {
        var id = UsuarioActual.Id(principal);
        if (id is null)
        {
            userId = Guid.Empty;
            return Results.Json(
                new { message = "Debes iniciar sesion para hacer esto." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        userId = id.Value;
        return null;
    }

    /// <summary>
    /// 404 para lo que no es de este usuario.
    /// </summary>
    /// <remarks>
    /// Se responde 404 y no 403 a proposito. Un 403 confirmaria que el recurso
    /// existe pero no es tuyo, lo cual ya es informacion: con solo probar
    /// identificadores, se podrian enumerar archivos ajenos. La version
    /// anterior hacia lo mismo y hay que mantenerlo.
    /// </remarks>
    public static IResult NoEncontrado() => Results.Json(
        new { message = "No encontrado." },
        statusCode: StatusCodes.Status404NotFound);

    public static IResult Error(string mensaje, int codigo) =>
        Results.Json(new { message = mensaje }, statusCode: codigo);
}
