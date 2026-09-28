using System.Net;
using System.Net.Http.Json;
using GestorArchivosBlzpg.Comun;
using Microsoft.AspNetCore.Components.Forms;

namespace GestorArchivosBlzpg.Comun;

/// <summary>
/// Cliente de la API de archivos y carpetas.
/// </summary>
/// <remarks>
/// Todas las llamadas van con <c>credentials: include</c>, que es lo que hace
/// el navegador mande la cookie de sesion. Sin eso, el servidor responderia 401
/// a todo aunque la sesion fuese valida.
/// </remarks>
public class ApiArchivos(HttpClient http)
{
    // --- Archivos -----------------------------------------------------------

    public async Task<ArchivoDto[]> Listar(
        Guid? carpetaId = null, string? categoria = null, string? busqueda = null,
        string? orden = null, bool descendente = false, CancellationToken ct = default)
    {
        var q = new List<string>();
        if (carpetaId is { } c) q.Add($"carpetaId={c}");
        if (!string.IsNullOrWhiteSpace(categoria)) q.Add($"categoria={Uri.EscapeDataString(categoria)}");
        if (!string.IsNullOrWhiteSpace(busqueda)) q.Add($"busqueda={Uri.EscapeDataString(busqueda)}");
        if (!string.IsNullOrWhiteSpace(orden)) q.Add($"orden={Uri.EscapeDataString(orden)}");
        if (descendente) q.Add("desc=true");

        var url = "/api/files" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
        var r = await http.GetFromJsonAsync<ListaArchivosDto>(url, ct);
        return r?.Files ?? [];
    }

    public async Task<EstadisticasDto?> Estadisticas(CancellationToken ct = default)
    {
        try
        {
            return await http.GetFromJsonAsync<EstadisticasDto>("/api/files/stats", ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sube un archivo.
    /// </summary>
    /// <remarks>
    /// El contenido va en <c>multipart/form-data</c>, no en JSON: es binario y
    /// hasta 15 MB, y codificarlo en base64 lo inflaria un tercio y lo pasaria
    /// por memoria dos veces.
    /// </remarks>
    public async Task<(bool Ok, string Mensaje, ArchivoDto? Archivo)> Subir(
        IBrowserFile archivo, Guid? carpetaId, string? descripcion, string? etiquetas,
        CancellationToken ct = default)
    {
        using var contenido = new MultipartFormDataContent();

        // Se copia el archivo a un MemoryStream porque el contenido multipart no
        // puede releer el stream del navegador: sin la copia, al enviarlo se
        // leeria una segunda vez y llegaria vacio.
        using var buffer = new MemoryStream();
        await archivo.OpenReadStream(MaxMB * 1024 * 1024 + 1024).CopyToAsync(buffer, ct);
        buffer.Position = 0;

        var parte = new ByteArrayContent(buffer.ToArray());
        parte.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(archivo.ContentType) ? "application/octet-stream" : archivo.ContentType);

        contenido.Add(parte, "archivo", archivo.Name);

        if (carpetaId is { } c) contenido.Add(new StringContent(c.ToString()), "folderId");
        if (!string.IsNullOrWhiteSpace(descripcion)) contenido.Add(new StringContent(descripcion), "description");
        if (!string.IsNullOrWhiteSpace(etiquetas)) contenido.Add(new StringContent(etiquetas), "tags");

        var r = await http.PostAsync("/api/files", contenido, ct);
        var cuerpo = await r.Content.ReadAsStringAsync(ct);

        if (r.IsSuccessStatusCode)
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<SubidaDto>(cuerpo);
            return (true, dto?.Message ?? "Archivo subido.", dto?.File);
        }

        return (false, ExtraerMensaje(cuerpo, r.StatusCode), null);
    }

    public async Task<(bool Ok, string Mensaje)> Editar(
        Guid id, string? nombre, string? descripcion, string[]? etiquetas, Guid? carpetaId)
    {
        var r = await http.PatchAsJsonAsync($"/api/files/{id}", new
        {
            name = nombre,
            description = descripcion,
            tags = etiquetas,
            folderId = carpetaId,
        });

        return (r.IsSuccessStatusCode, ExtraerMensaje(await r.Content.ReadAsStringAsync(), r.StatusCode));
    }

    public async Task<(bool Ok, string Mensaje)> Eliminar(Guid id)
    {
        var r = await http.DeleteAsync($"/api/files/{id}");
        return (r.IsSuccessStatusCode, ExtraerMensaje(await r.Content.ReadAsStringAsync(), r.StatusCode));
    }

    /// <summary>
    /// Abre la URL de descarga de un archivo.
    /// </summary>
    /// <remarks>
    /// No se descarga con fetch sino devolviendo la URL, para que el navegador
    /// lo haga con su propio descargador: puede poner la descarga en segundo
    /// plano, avisar de cuando ha terminado, y usar el nombre real del archivo.
    /// Con fetch habria que leer los bytes en memoria y montarlos a mano, que
    /// para 15 MB es una copia inutil.
    ///
    /// La cookie viaja sola porque la peticion la hace el navegador al pulsar.
    /// </remarks>
    public string UrlDescarga(Guid id) => $"/api/files/{id}/download";


    // --- Carpetas -----------------------------------------------------------

    public async Task<CarpetaDto[]> Carpetas(CancellationToken ct = default)
    {
        var r = await http.GetFromJsonAsync<ListaCarpetasDto>("/api/folders", ct);
        return r?.Folders ?? [];
    }

    public async Task<(bool Ok, string Mensaje)> CrearCarpeta(string nombre, string? descripcion, string color)
    {
        var r = await http.PostAsJsonAsync("/api/folders", new
        {
            name = nombre,
            description = descripcion,
            color,
        });

        return (r.IsSuccessStatusCode, ExtraerMensaje(await r.Content.ReadAsStringAsync(), r.StatusCode));
    }

    public async Task<(bool Ok, string Mensaje)> RenombrarCarpeta(Guid id, string nombre)
    {
        var r = await http.PatchAsJsonAsync($"/api/folders/{id}", new { name = nombre });
        return (r.IsSuccessStatusCode, ExtraerMensaje(await r.Content.ReadAsStringAsync(), r.StatusCode));
    }

    public async Task<(bool Ok, string Mensaje)> EliminarCarpeta(Guid id)
    {
        var r = await http.DeleteAsync($"/api/folders/{id}");
        return (r.IsSuccessStatusCode, ExtraerMensaje(await r.Content.ReadAsStringAsync(), r.StatusCode));
    }

    // --- Utilidades ---------------------------------------------------------

    /// <summary>
    /// Saca el mensaje de un cuerpo de error.
    /// </summary>
    /// <remarks>
    /// Los 413 llevan el motivo concreto (supera el limite por archivo, o no cabe
    /// en la cuota), y es el caso que mas aparece: teaching ese texto es lo que
    /// permite entender por que la subida no entra.
    /// </remarks>
    private static string ExtraerMensaje(string cuerpo, HttpStatusCode codigo)
    {
        try
        {
            var dto = System.Text.Json.JsonSerializer.Deserialize<MensajeDto>(cuerpo);
            if (!string.IsNullOrWhiteSpace(dto?.Message)) return dto.Message;
        }
        catch (System.Text.Json.JsonException)
        {
            // Cuerpo que no es JSON: cae al texto de abajo.
        }

        return codigo switch
        {
            HttpStatusCode.Unauthorized => "Debes iniciar sesion.",
            HttpStatusCode.Forbidden => "No tienes permiso para esa operacion.",
            HttpStatusCode.NotFound => "No encontrado.",
            HttpStatusCode.Conflict => "Ya existe algo con ese nombre.",
            HttpStatusCode.RequestEntityTooLarge => "El archivo es demasiado grande.",
            _ => "No se ha podido completar la operacion.",
        };
    }

    /// <summary>
    /// Tope por archivo, en MB.
    /// </summary>
    /// <remarks>
    /// Va en el cliente para no dejar elegir un archivo de 40 MB que el servidor
    /// va a rechazar. La comprobacion real la hace el servidor igualmente: este
    /// es solo para dar la sensacion inmediata, no la garantia.
    /// </remarks>
    private const int MaxMB = 15;
}
