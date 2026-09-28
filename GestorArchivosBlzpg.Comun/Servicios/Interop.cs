using Microsoft.JSInterop;

namespace GestorArchivosBlzpg.Comun;

/// <summary>
/// Acceso al JavaScript del navegador.
/// </summary>
/// <remarks>
/// Existe un unico punto de entrada al JavaScript, y no llamadas sueltas por los
/// componentes. Motivo: asi el modulo se importa una sola vez y su
/// <c>IJSObjectReference</c> tiene un unico ciclo de vida. Repartirlo haria que
/// cada componente gestionara el suyo, y no liberarlos es una fuga tipica.
///
/// El modulo se libera cuando termina el circuito.
/// </remarks>
public sealed class Interop(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _modulo;
    private bool _descargado;

    /// <summary>
    /// Pregunta al usuario y devuelve si confirma.
    /// </summary>
    /// <remarks>
    /// Si el modulo no carga o la llamada falla, se responde que no. Es lo
    /// prudente: ante un fallo al preguntar, lo que no debe pasar es que un
    /// borrado se ejecute sin que nadie lo haya confirmado.
    ///
    /// Ese comportamiento tiene una contrapartida que conviene conocer: si algo
    /// va mal aqui, el sintoma es que borrar no hace nada, sin ningun aviso. Por
    /// eso el modulo de JavaScript tiene que exportar las funciones de verdad.
    /// </remarks>
    public async Task<bool> Confirmar(string texto)
    {
        try
        {
            _modulo ??= await js.InvokeAsync<IJSObjectReference>("import", "./confirmar.js");
            return await _modulo.InvokeAsync<bool>("confirmar", texto);
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"No se pudo preguntar al usuario: {ex.Message}");
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
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
                // El navegador ya no esta conectado: es el camino normal al
                // cerrar la pestana, y Dispose forma parte de el.
            }
        }
    }
}
