using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GestorArchivosBlzpg.Comun;

// Arranque del runtime de WebAssembly en el navegador.
//
// Este proyecto no registra componentes raiz. En un Blazor Web App con modo de
// render, el montaje lo hace el servidor: App.razor, que esta en el proyecto
// servidor, emite el documento con marcadores y el navegador solo los
// sustituye.
//
// Registrar aqui RootComponents.Add es del modelo standalone, y en este
// duplicaria el montaje: el servidor dejaria un marcador, el runtime montaria
// otra vez por su cuenta, y cada elemento apareceria dos veces. El sintoma es
// desconcertante porque nada falla en la consola: la pantalla se ve bien y
// solo esta repetida.
//
// Los componentes van repartidos entre los dos proyectos: App.razor en el
// servidor, y el layout y las paginas aqui, porque un componente con modo
// WebAssembly tiene que compilarse en el proyecto cliente para que el navegador
// lo descargue.
//
// Las cookies viajan solas porque la API se sirve desde el mismo origen. No hace
// falta un manejador que reintente al recibir un 401, como en las plantillas que
// guardan un token en el almacenamiento del navegador: aqui la sesion es una
// cookie httpOnly que el codigo no puede leer ni alterar.
var builder = WebAssemblyHostBuilder.CreateDefault(args);


builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

// Los componentes los ejecuta este runtime, y para entonces los usa este
// contenedor, no el del servidor. Si aqui no se registran, Blazor falla al
// crearlos con un error que solo dice que no hay un servicio de ese tipo, sin
// recordar que en el servidor si lo hay.
builder.Services.AddScoped<ApiArchivos>();
builder.Services.AddScoped<Sesion>();
builder.Services.AddScoped<ProveedorAutenticacion>();
builder.Services.AddScoped<Interop>();
builder.Services.AddScoped<Tema>();
builder.Services.AddAuthorizationCore();

await builder.Build().RunAsync();
