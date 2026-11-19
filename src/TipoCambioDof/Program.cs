using System.Runtime.InteropServices;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TipoCambioDof;
using TipoCambioDof.Alertas;
using TipoCambioDof.Banxico;
using TipoCambioDof.Datos;
using TipoCambioDof.Procesos;

const int Ok = 0, Error = 1, ConAlertas = 2, Uso = 64;

var comando = Comando.Parsear(args);
if (comando is null)
{
    Console.Error.WriteLine($"Argumentos inválidos: {string.Join(' ', args)}\n");
    Console.Error.WriteLine(Comando.Ayuda);
    return Uso;
}
if (comando.Tipo == TipoComando.Ayuda)
{
    Console.WriteLine(Comando.Ayuda);
    return Ok;
}

// Cancelación limpia con `docker stop` (SIGTERM) o Ctrl+C, importante durante la espera entre reintentos.
using var cts = new CancellationTokenSource();
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; cts.Cancel(); });
using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; cts.Cancel(); });

// Fuera de Docker (depuración local) toma la configuración del .env del proyecto.
var archivoEnv = ArchivoEnv.Cargar();

var builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    o.IncludeScopes = false;
});
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

var cfg = Configuracion.Desde(builder.Configuration);
builder.Services.AddSingleton(cfg);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Reloj>();
builder.Services.AddSingleton<RepositorioTipoCambio>();
builder.Services.AddSingleton<RepositorioLegados>();
builder.Services.AddSingleton<Alertador>();
builder.Services.AddSingleton<Sincronizador>();
builder.Services.AddSingleton<ProcesoDiario>();
builder.Services.AddSingleton<ProcesoHistorico>();
builder.Services
    .AddHttpClient<BanxicoClient>(c =>
    {
        c.BaseAddress = BanxicoClient.UrlBase;
        c.Timeout = TimeSpan.FromSeconds(30);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    // Una serie inexistente responde 302: no seguir la redirección para poder tratarla como error.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient(Alertador.ClienteHttp, c => c.Timeout = TimeSpan.FromSeconds(10));

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("TipoCambioDof");
if (archivoEnv is not null)
    log.LogInformation("Configuración local cargada de {Archivo}.", archivoEnv);

try
{
    cfg.ExigirCadenaConexion();
    switch (comando.Tipo)
    {
        case TipoComando.CrearTabla:
            await host.Services.GetRequiredService<RepositorioTipoCambio>().CrearEsquemaAsync(cts.Token);
            log.LogInformation("Esquema verificado/creado.");
            break;
        case TipoComando.Diario:
            cfg.ExigirToken();
            if (cfg.ActualizarLegados)
                cfg.ExigirLegados();
            await host.Services.GetRequiredService<ProcesoDiario>().EjecutarAsync(cts.Token);
            break;
        case TipoComando.Historico:
            cfg.ExigirToken();
            await host.Services.GetRequiredService<ProcesoHistorico>()
                .EjecutarAsync(comando.Desde, comando.Hasta, cts.Token);
            break;
    }

    return host.Services.GetRequiredService<Alertador>().Enviadas > 0 ? ConAlertas : Ok;
}
catch (OperationCanceledException) when (cts.IsCancellationRequested)
{
    log.LogWarning("Ejecución cancelada.");
    return Error;
}
catch (Exception ex) when (ex is ConfiguracionInvalidaException or BanxicoException or SqlException or HttpRequestException)
{
    log.LogError("{Tipo}: {Mensaje}", ex.GetType().Name, ex.Message);
    await host.Services.GetRequiredService<Alertador>()
        .EnviarAsync($"La corrida '{comando.Tipo}' falló: {ex.Message}", CancellationToken.None);
    return Error;
}
catch (Exception ex)
{
    log.LogCritical(ex, "Error no controlado.");
    return Error;
}
