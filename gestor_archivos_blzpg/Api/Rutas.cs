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

        // Sesion. Sin antiforgery en ninguna de estas cuatro.
        //
        // En Blazor, el token antiforgery viaja en una cookie aparte y en un
        // campo del formulario, y hay que aadirlo a mano con HttpClient. Con
        // sesion por cookie httpOnly no hace falta: un tercero no puede alterar
        // nada, porque el navegador solo envia lo que el servidor le puso.
        api.MapPost("/auth/register", RutasAuth.Registrar).DisableAntiforgery();
        api.MapPost("/auth/login", RutasAuth.Login).DisableAntiforgery();
        api.MapPost("/auth/logout", RutasAuth.Logout).DisableAntiforgery();
        api.MapGet("/auth/me", RutasAuth.Yo);

        api.MapGet("/files", RutasArchivos.Listar);
        api.MapGet("/files/stats", RutasArchivos.Estadisticas);

        // multipart/form-data: el cuerpo lleva el archivo binario y los campos
        // del formulario, no JSON.
        api.MapPost("/files", RutasArchivos.Subir).DisableAntiforgery();

        api.MapGet("/files/{id:guid}/download", RutasArchivos.Descargar);
        api.MapGet("/files/{id:guid}/preview", RutasArchivos.Previsualizar);

        // Estas dos tambien necesitan DisableAntiforgery. Sin el, el middleware
        // de antiforgery rechaza PATCH y DELETE con un 400 antes de que la ruta
        // se mire, con un error que no nombra ni el metodo ni la ruta.
        api.MapPatch("/files/{id:guid}", RutasArchivos.Editar).DisableAntiforgery();
        api.MapDelete("/files/{id:guid}", RutasArchivos.Eliminar).DisableAntiforgery();

        api.MapGet("/folders", RutasCarpetas.Listar);
        api.MapPost("/folders", RutasCarpetas.Crear).DisableAntiforgery();
        api.MapPatch("/folders/{id:guid}", RutasCarpetas.Renombrar).DisableAntiforgery();
        api.MapDelete("/folders/{id:guid}", RutasCarpetas.Eliminar).DisableAntiforgery();
    }
}
