using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using GestorArchivosBlzpg.Api;
using GestorArchivosBlzpg.Components;
using GestorArchivosBlzpg.Config;
using GestorArchivosBlzpg.Data;
using GestorArchivosBlzpg.Storage;

var builder = WebApplication.CreateBuilder(args);

// El archivo de secretos locales se carga si existe, y antes que el resto, para
// que sus valores tengan prioridad sobre appsettings.json. Va en .gitignore, y
// es donde se pondra la cadena de conexion y la clave del almacen sin tocar el
// archivo que si se versiona.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ---------------------------------------------------------------------------
// Modo de acceso
// ---------------------------------------------------------------------------
// Se lee antes de registrar nada, y falla si el valor no es valido. Es
// deliberado: arrancar con un modo inventado podria dejar abierto un metodo de
// acceso que se pretendia cerrado, y eso no se ve hasta que alguien lo usa.
var modoAcceso = ModosAcceso.Leer(builder.Configuration);
var config = new Configuracion(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Falta la cadena de conexion a PostgreSQL. Defineló¡la en " +
        "appsettings.Local.json, o con la variable de entorno " +
        "ConnectionStrings__Postgres. Ver appsettings.example.json."
    );
}

// ---------------------------------------------------------------------------
// Base de datos
// ---------------------------------------------------------------------------
// El tamano del pool no se ajusta aqui: con EF Core se controla por la cadena
// de conexion (Maximum Pool Size). En desarrollo el valor por defecto de Npgsql
// (100) sobra de largo; en produccion conviene bajarlo, porque cada conexion
// abierta es un proceso del servidor de PostgreSQL.
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------
// Sesion
// ---------------------------------------------------------------------------
// Cookie de ASP.NET Core, no JWT propio. La escribe el servidor, es httpOnly (el
// codigo del navegador no puede leerla) y el navegador la manda sola en cada
// peticion. La version con Node hacia esto a mano con un JWT firmado; aqui lo da
// el marco y sale mejor, porque el secreto de la cookie lo genera y custodia el
// propio ASP.NET Core, con proteccion de datos y rotacion.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.Cookie.Name = "gestor_archivos_blzpg";
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SameSite = SameSiteMode.Lax;

        // secure en produccion. En local se deja como venga la peticion, porque
        // el desarrollo va por http y con la marca puesta el navegador no
        // guardaria la cookie y no habria forma de entrar.
        opt.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        opt.ExpireTimeSpan = TimeSpan.FromDays(7);
        opt.SlidingExpiration = true;

        // Sin redireccion a una pagina de acceso: esto es una API. Si se
        // redirige, una llamada con fetch recibe HTML donde esperaba JSON, y el
        // error sale como "fallo de formato" en vez de "no has iniciado
        // sesion", que es lo que hay que saber para arreglarlo.
        opt.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        opt.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ---------------------------------------------------------------------------
// Almacenamiento de archivos
// ---------------------------------------------------------------------------
// El backend se elige con STORAGE_BACKEND y se valida aqui, no en cada llamada,
// para que un valor mal escrito se note al arrancar y no en la primera subida.
var backend = builder.Configuration["STORAGE_BACKEND"] ?? "local";
switch (backend.ToLowerInvariant())
{
    case "local":
        builder.Services.Configure<OpcionesAlmacenLocal>(
            builder.Configuration.GetSection("AlmacenLocal"));
        builder.Services.AddSingleton<IAlmacen, AlmacenLocal>();
        break;

    case "supabase":
        var supa = new OpcionesSupabase();
        builder.Configuration.GetSection("Supabase").Bind(supa);

        if (string.IsNullOrWhiteSpace(supa.Url) || string.IsNullOrWhiteSpace(supa.ServiceRoleKey))
        {
            throw new InvalidOperationException(
                "STORAGE_BACKEND=supabase, pero faltan Supabase:Url o " +
                "Supabase:ServiceRoleKey. Ver appsettings.example.json."
            );
        }

        builder.Services.Configure<OpcionesSupabase>(o =>
        {
            o.Url = supa.Url;
            o.ServiceRoleKey = supa.ServiceRoleKey;
            o.Bucket = supa.Bucket;
        });
        builder.Services.AddSingleton<IAlmacen, AlmacenSupabase>();
        break;

    default:
        throw new InvalidOperationException(
            $"STORAGE_BACKEND={backend} no es valido. Usa \"local\" o \"supabase\".");
}

builder.Services.AddHttpClient("supabase")
    .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromMinutes(2));

// Un almacen por peticion: el de disco no tiene estado y el de Supabase envuelve
// un HttpClient, que si se compartiera entre peticiones cruzaria cabeceras.
builder.Services.AddScoped<ServicioArchivos>();
builder.Services.AddScoped<ServicioCarpetas>();
builder.Services.AddScoped<ConsultaArchivos>();
builder.Services.AddSingleton(config);

// El limite de subida se deja un poco por encima del tope por archivo, para que
// uno justo en el limite llegue entero y con sitio para los campos del
// formulario. Si el limite de la aplicacion sube, este numero sube con el.
var limiteSubida = AppLimits.MaxFileSize(config) + (2 * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = limiteSubida);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = limiteSubida);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// La API se registra antes que los componentes, para que /api/... no se
// interprete como una ruta de pagina.
app.MapApi();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(GestorArchivosBlzpg.Client._Imports).Assembly);

// Avisos de arranque, a la consola: en un servidor es donde se mira cuando algo
// no arranca, y es lo primero que se lee en el registro.
Console.WriteLine("CloudVault - PostgreSQL + Blazor WebAssembly");
Console.WriteLine($"  Modo de acceso:    {modoAcceso.Texto()} ({modoAcceso})");
Console.WriteLine($"  Almacenamiento:    {backend}");
Console.WriteLine(
    $"  Limites:           {AppLimits.MaxFileSize(config) / (1024 * 1024)} MB por archivo, " +
    $"{AppLimits.UserQuotaBytes(config) / (1024 * 1024)} MB por cuenta, " +
    $"aviso al {AppLimits.QuotaWarnPercent(config):0}%");

app.Run();
