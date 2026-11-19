using System.Net;
using Microsoft.Extensions.Logging;

namespace TipoCambioDof.Banxico;

public sealed class BanxicoException(string mensaje, bool transitorio = false) : Exception(mensaje)
{
    public bool Transitorio { get; } = transitorio;
}

/// <summary>Cliente de la API SIE de Banxico para la serie SF60653 (README §4).</summary>
public sealed class BanxicoClient(HttpClient http, Configuracion cfg, ILogger<BanxicoClient> log)
{
    public const string Serie = "SF60653";
    public static readonly Uri UrlBase = new("https://www.banxico.org.mx/SieAPIRest/service/v1/");

    // README §5 paso 2: 3 reintentos con espera creciente ante errores de red o HTTP 5xx.
    private static readonly TimeSpan[] Esperas =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30)];

    public async Task<ResultadoSie> ObtenerAsync(DateOnly inicio, DateOnly fin, CancellationToken ct)
    {
        var ruta = $"series/{Serie}/datos/{inicio:yyyy-MM-dd}/{fin:yyyy-MM-dd}";

        for (var intento = 0; ; intento++)
        {
            try
            {
                return await ConsultarAsync(ruta, ct);
            }
            catch (Exception ex) when (EsTransitorio(ex, ct) && intento < Esperas.Length)
            {
                log.LogWarning("Error transitorio consultando Banxico ({Error}). Reintento {N} de {Total} en {Espera}s.",
                    ex.Message, intento + 1, Esperas.Length, Esperas[intento].TotalSeconds);
                await Task.Delay(Esperas[intento], ct);
            }
        }
    }

    private async Task<ResultadoSie> ConsultarAsync(string ruta, CancellationToken ct)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        // El token va en header (no en query string) para que no quede en logs.
        solicitud.Headers.Add("Bmx-Token", cfg.TokenBanxico);

        using var respuesta = await http.SendAsync(solicitud, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        var codigo = (int)respuesta.StatusCode;

        if (respuesta.StatusCode == HttpStatusCode.OK)
            return RespuestaSie.Parsear(cuerpo, Serie);

        if (codigo >= 500)
            throw new BanxicoException($"HTTP {codigo}: {RespuestaSie.MensajeError(cuerpo)}", transitorio: true);

        if (codigo is >= 300 and < 400)
            throw new BanxicoException($"HTTP {codigo} (redirección): la serie {Serie} no existe o cambió la URL.");

        throw new BanxicoException($"HTTP {codigo}: {RespuestaSie.MensajeError(cuerpo)}");
    }

    private static bool EsTransitorio(Exception ex, CancellationToken ct) => ex switch
    {
        BanxicoException b => b.Transitorio,
        HttpRequestException => true,
        TaskCanceledException => !ct.IsCancellationRequested, // timeout del HttpClient
        _ => false,
    };
}
