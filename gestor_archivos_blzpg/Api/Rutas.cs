using GestorArchivosBlzpg.Data;

namespace GestorArchivosBlzpg.Api;

/// <summary>
/// Registra las rutas de la API.
/// </summary>
public static class Rutas
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Sesion. Sin antiforgery porque el token va en la cookie httpOnly y no
        // en un parametro: no hay estado que un tercero pueda alterar desde la
        // pagina para que el navegador lo envie por su cuenta, que es
        // precisamente lo que evita el antiforgery.
        api.MapPost("/auth/register", RutasAuth.Registrar);
        api.MapPost("/auth/login", RutasAuth.Login);
        api.MapPost("/auth/logout", RutasAuth.Logout);
        api.MapGet("/auth/me", RutasAuth.Yo);

        // Archivos.
        api.MapGet("/files", RutasArchivos.Listar);
        api.MapGet("/files/stats", RutasArchivos.Estadisticas);

        // multipart/form-data para la subida: el cuerpo lleva el archivo binario
        // y los campos del formulario, no JSON.
        api.MapPost("/files", RutasArchivos.Subir)
            .DisableAntiforgery();

        api.MapGet("/files/{id:guid}/download", RutasArchivos.Descargar);
        api.MapGet("/files/{id:guid}/preview", RutasArchivos.Previsualizar);
        api.MapPatch("/files/{id:guid}", RutasArchivos.Editar);
        api.MapDelete("/files/{id:guid}", RutasArchivos.Eliminar);

        // Carpetas.
        api.MapGet("/folders", RutasCarpetas.Listar);
        api.MapPost("/folders", RutasCarpetas.Crear);
        api.MapPatch("/folders/{id:guid}", RutasCarpetas.Renombrar);
        api.MapDelete("/folders/{id:guid}", RutasCarpetas.Eliminar);
    }
}
