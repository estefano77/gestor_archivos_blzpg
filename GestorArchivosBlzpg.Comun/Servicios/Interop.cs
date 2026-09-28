using Microsoft.JSInterop;

namespace GestorArchivosBlzpg.Comun;

/// <summary>
/// Acceso al JavaScript del navegador.
/// </summary>
/// <remarks>
/// Existe un unico punto de entrada al JavaScript, y no llamadas
/// <c>InvokeAsync</c> sueltas por los componentes. Motivo: estas funciones se
/// usan en varios sitios y, si se importara el modulo en cada uno, cada
/// componente manejaria su propio <c>IJSObjectReference</c> con su ciclo de vida.
/// No liberarlos es un caso tipico de fuga en Blazor.
///
/// El modulo se importa una vez y se guarda. Se libera cuando termina el
/// circuito, que es cuando Blazor descarta el navegador.
/// </remarks>
public sealed class Interop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _modulo;
    private bool _descargado;

    /// <summary>El modulo de JavaScript, importado la primera vez.</summary>
    private async ValueTask<IJSObjectReference> Modulo()
    {
        if (_modulo is not null) return _modulo;

        _modulo = await js.InvokeAsync<IJSObjectReference>("import", "./confirmar.js");
        return _modulo;
    }

    /// <summary>
    /// Pregunta al usuario y devuelve si confirma.
    /// </summary>
    /// <remarks>
    /// Si el modulo no se puede cargar, se responde que no. Es lo prudente: ante
    /// un fallo al preguntar, lo que no debe pasar es que un borrado se ejecute
    /// sin que nadie lo haya confirmado.
    /// </remarks>
    public async Task<bool> Confirmar(string texto, bool enNavegador = true)
    {
        if (!enNavegador) return false;
        try
        {
            var modulo = await Modulo();
            return await modulo.InvokeAsync<bool>("confirmar", texto);
        }
        catch (JSException)
        {
            Console.Error.WriteLine("No se pudo cargar confirmar.js; se responde que no.");
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Cambia el titulo de la pestana.
    /// </summary>
    /// <remarks>
    /// No se usa el componente <c>PageTitle</c> de Blazor porque ese publica en
    /// el <c>HeadOutlet</c>, que usa un identificador de seccion fijo: si dos
    /// paginas lo usan a la vez, Blazor falla con "There is already a subscriber
    /// to the content with the given section ID" y tumba el render. Desde fuera
    /// se ve como una pagina normal en la que no responde ningun boton.
    ///
    /// Se hace con JavaScript, y hay que comprobar antes si la aplicacion ya esta
    /// en el navegador. Durante el prerender no lo esta, y llamar a JavaScript ahi
    /// lanza "JavaScript interop calls cannot be issued during server-side static
    /// rendering", que tumba la pagina entera con un error 500.
    ///
    /// El titulo viaja como parametro de la funcion, no interpolado en una
    /// cadena: un nombre con comillas lo romperia.
    /// </remarks>
    public async Task PonerTitulo(string titulo, bool enNavegador = true)
    {
        if (!enNavegador) return;
        try
        {
            var modulo = await Modulo();
            await modulo.InvokeVoidAsync("ponerTitulo", titulo);
        }
        catch (JSException)
        {
            // El titulo no es esencial: si falla, la pagina funciona igual.
        }
    }

    /// <summary>Registra el manejador de errores de Blazor, si se puede.</summary>
    public async Task OcultarError(bool enNavegador = true)
    {
        if (!enNavegador) return;
        try
        {
            var modulo = await Modulo();
            await modulo.InvokeVoidAsync("blazor-error-ui", "remove");
        }
        catch (JSException)
        {
            // Nada que hacer: solo es el aviso de error.
        }
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
                // El navegador ya no esta conectado. Es normal al cerrar la
                // pestana, y Dispose forma parte de ese camino.
            }
        }
    }
}
