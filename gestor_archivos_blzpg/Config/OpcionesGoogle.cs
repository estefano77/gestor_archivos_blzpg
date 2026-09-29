namespace GestorArchivosBlzpg.Config;

/// <summary>
/// Credenciales del cliente OAuth de Google.
/// </summary>
public class OpcionesGoogle
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// URI de redireccion que se registra en Google Cloud.
    /// </summary>
    /// <remarks>
    /// Google exige que sea **identica** en la peticion de autorizacion y en el
    /// canje del codigo. Si cada paso la calculara por su cuenta, entrar por un
    /// dominio con o sin "www" romperia el canje con un error que no explica
    /// nada. Dejarla fija en la configuracion es lo recomendado en produccion;
    /// si esta vacia, se deduce de la peticion, que es lo comodo en desarrollo.
    /// </remarks>
    public string RedirectUri { get; set; } = "";

    public bool Configurado =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
