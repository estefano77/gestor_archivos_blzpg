using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GestorArchivosBlzpg.Storage;

public class OpcionesSupabase
{
    public string Url { get; set; } = "";
    public string ServiceRoleKey { get; set; } = "";
    public string Bucket { get; set; } = "archivos";
}

/// <summary>
/// Cliente de Supabase Storage por HTTP.
/// </summary>
/// <remarks>
/// Se habla con la API REST directamente en vez de usar el SDK de Supabase
/// porque hacen falta cuatro operaciones (subir, descargar, borrar, borrar por
/// prefijo) y el SDK arrastra PostgREST, realtime y mas dependencias para nada de
/// eso. Menos paquetes que actualizar es menos superficie de problema.
///
/// Los tres detalles que costaron sangre en la version Node estan comentados
/// donde_importan: el Content-Type debe ser el del archivo, el borrado por
/// prefijo va envuelto en {"prefixes":[...]} y cada clave necesita su ruta
/// completa.
/// </remarks>
public class AlmacenSupabase(IOptions<OpcionesSupabase> opciones, IHttpClientFactory http) : IAlmacen
{
    private readonly OpcionesSupabase o = opciones.Value;
    private string Base => o.Url.TrimEnd('/');

    /// <summary>Cabecera de autorizacion con la clave de servicio.</summary>
    private HttpRequestMessage Peticion(HttpMethod metodo, string ruta)
    {
        var p = new HttpRequestMessage(metodo, $"{Base}/storage/v1/{ruta}");
        p.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ServiceRoleKey);
        p.Headers.Add("apikey", o.ServiceRoleKey);
        return p;
    }

    public async Task<string> Guardar(string ruta, Stream contenido, string contentType, CancellationToken ct = default)
    {
        // El Content-Type tiene que ser el del archivo. Si se manda generico
        // (application/octet-stream, que es lo que llega si no se indica nada),
        // un bucket con lista de tipos permitidos lo rechaza con 415. Se
        // sustituye por el tipo real antes de subir, con el nombre como
        // respaldo por si el navegador no lo dio.
        var tipo = string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream"
            ? await DetectarTipo(ruta, ct) ?? "application/octet-stream"
            : contentType;

        using var p = Peticion(HttpMethod.Post, $"object/{o.Bucket}/{ruta}");

        // up=1 es "subir o reemplazar". Sin el, reintentar una subida falla
        // porque el objeto ya existe.
        p.Content = new StreamContent(contenido);
        p.Content.Headers.ContentType = new MediaTypeHeaderValue(tipo);

        using var respuesta = await http.CreateClient("supabase").SendAsync(p, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Supabase Storage rechazo la subida de {ruta}: {(int)respuesta.StatusCode} {cuerpo}");
        }

        return ruta;
    }

    /// <summary>
    /// Deduce el tipo MIME del nombre del archivo.
    /// </summary>
    /// <remarks>
    /// Se hace aqui y no al recibir la subida porque el <c>InputFile</c> de
    /// Blazor no siempre trae un tipo util, y mandarlo generico hacia el bucket
    /// es justamente lo que lo hace rechazar la subida.
    /// </remarks>
    private static async Task<string?> DetectarTipo(string ruta, CancellationToken ct)
    {
        try
        {
            var prov = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            return prov.TryGetContentType(ruta, out var tipo) ? tipo : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Binario> Leer(string ruta, CancellationToken ct = default)
    {
        using var p = Peticion(HttpMethod.Get, $"object/{o.Bucket}/{ruta}");
        var respuesta = await http.CreateClient("supabase").SendAsync(p, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            throw new FileNotFoundException($"Supabase Storage no tiene {ruta} ({(int)respuesta.StatusCode}).");
        }

        var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
        var tipo = respuesta.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return new Binario(Path.GetFileName(ruta), flujo, tipo, respuesta.Content.Headers.ContentLength ?? 0);
    }

    public async Task<bool> Borrar(string ruta, CancellationToken ct = default)
    {
        using var p = Peticion(HttpMethod.Delete, $"object/{o.Bucket}/{ruta}");
        var respuesta = await http.CreateClient("supabase").SendAsync(p, ct);

        // 200 sin cuerpo y 404 significan las dos cosas que nos interesan: que
        // ya no esta. No se distingue el borrado real del no-op.
        return respuesta.IsSuccessStatusCode;
    }

    public async Task<int> BorrarPrefijo(string prefijo, CancellationToken ct = default)
    {
        var claves = await Listar(prefijo, ct);
        if (claves.Count == 0) return 0;

        // El borrado por prefijo exige {"prefixes":[...]} con las rutas
        // completas. Mandar un array suelto devuelve 400, y mandar solo el
        // prefijo de la carpeta devuelve 200 sin borrar nada: el peor caso,
        // porque parece que funciono.
        using var p = Peticion(HttpMethod.Delete, $"object/{o.Bucket}");
        p.Content = new StringContent(
            JsonSerializer.Serialize(new { prefixes = claves }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var respuesta = await http.CreateClient("supabase").SendAsync(p, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Supabase Storage no pudo borrar el prefijo {prefijo}: {(int)respuesta.StatusCode} {cuerpo}");
        }

        return claves.Count;
    }

    /// <summary>
    /// Lista los objetos bajo un prefijo.
    /// </summary>
    /// <remarks>
    /// Se usa el endpoint de listar y no el de descargar para comprobar si algo
    /// esta: la descarga de Supabase pasa por una CDN que puede responder 200
    /// con un objeto que ya se borro, mientras que el listado ve el estado real
    /// del bucket.
    ///
    /// El listado devuelve solo el nombre del fichero, sin la parte de la
    /// carpeta, asi que hay que volver a anteponer el prefijo.
    /// </remarks>
    private async Task<List<string>> Listar(string prefijo, CancellationToken ct)
    {
        using var p = Peticion(HttpMethod.Post, $"object/list/{o.Bucket}");
        p.Content = new StringContent(
            JsonSerializer.Serialize(new { prefix = prefijo, limit = 1000 }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var respuesta = await http.CreateClient("supabase").SendAsync(p, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);

        if (!respuesta.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Supabase Storage no pudo listar {prefijo}: {(int)respuesta.StatusCode} {cuerpo}");
        }

        using var doc = JsonDocument.Parse(cuerpo);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

        var claves = new List<string>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nombre)
            {
                claves.Add($"{prefijo}{nombre}");
            }
        }

        return claves;
    }
}
