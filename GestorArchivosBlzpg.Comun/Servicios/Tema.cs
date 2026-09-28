using Microsoft.JSInterop;

namespace GestorArchivosBlzpg.Comun;

/// <summary>
/// Tema claro u oscuro.
/// </summary>
/// <remarks>
/// El tema se guarda en el almacenamiento del navegador, que es lo unico que
/// sobrevive a un F5. Es la unica parte de la aplicacion que guarda algo ahi,
/// y no es un secreto: es una preferencia de aspecto.
///
/// El atributo <c>dark</c> va en el elemento raiz, no en una clase del cuerpo.
/// Es como lo hace la version con React, y hay una razon: los selectores del
/// tema oscuro se escriben como <c>.dark .algo</c>, y si la clase estuviera en
/// el cuerpo habria que anadirla al CSS especificandolo.
/// </remarks>
public sealed class Tema(IJSRuntime js) : IAsyncDisposable
{
    private const string Clave = "tema";
    private IJSObjectReference? _modulo;
    private bool _descargado;

    /// <summary>Tema actual: "claro" u "oscuro".</summary>
    public string Actual { get; private set; } = "claro";

    public bool EsOscuro => Actual == "oscuro";

    /// <summary>
    /// Aplica el tema guardado y devuelve cual es.
    /// </summary>
    /// <remarks>
    /// Se llama al arrancar, antes de pintar nada, para que no haya un
    /// parpadeo de tema claro en alguien que lo tiene en oscuro.
    /// </remarks>
    public async Task<string> Inicializar()
    {
        try
        {
            var modulo = await Modulo();
            Actual = await modulo.InvokeAsync<string>("obtenerTema") ?? "claro";
            await modulo.InvokeVoidAsync("aplicarTema", Actual);
        }
        catch (JSException)
        {
            // Sin JavaScript no hay tema, pero la aplicacion sigue funcionando.
            Actual = "claro";
        }

        return Actual;
    }

    /// <summary>Cambia entre tema claro y oscuro, y lo guarda.</summary>
    public async Task Alternar()
    {
        Actual = EsOscuro ? "claro" : "oscuro";

        try
        {
            var modulo = await Modulo();
            await modulo.InvokeVoidAsync("aplicarTema", Actual);
        }
        catch (JSException)
        {
        }
    }

    private async ValueTask<IJSObjectReference> Modulo()
    {
        if (_modulo is not null) return _modulo;
        _modulo = await js.InvokeAsync<IJSObjectReference>("import", "./tema.js");
        return _modulo;
    }

    public async ValueTask DisposeAsync()
    {
        if (_descargado) return;
        _descargado = true;

        if (_modulo is not null)
        {
            try
            {
                await _modulo.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // El navegador ya no esta conectado: normal al cerrar la pestana.
            }
        }
    }
}
