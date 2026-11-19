using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace TipoCambioDof.Alertas;

/// <summary>
/// Registra cada alerta en el log (nivel Error) y, si existe ALERT_WEBHOOK_URL, la envía como
/// <c>{"text": "..."}</c> por POST (formato que aceptan los webhooks entrantes de Slack y Teams).
/// Si hubo alertas, el proceso termina con código 2.
/// </summary>
public sealed class Alertador(IHttpClientFactory fabrica, Configuracion cfg, ILogger<Alertador> log)
{
    public const string ClienteHttp = "alertas";

    public int Enviadas { get; private set; }

    public async Task EnviarAsync(string mensaje, CancellationToken ct)
    {
        Enviadas++;
        log.LogError("ALERTA: {Mensaje}", mensaje);

        if (cfg.WebhookAlertas is null)
            return;

        try
        {
            var http = fabrica.CreateClient(ClienteHttp);
            using var resp = await http.PostAsJsonAsync(cfg.WebhookAlertas,
                new { text = $"[tipo-cambio-dof] {mensaje}" }, ct);
            if (!resp.IsSuccessStatusCode)
                log.LogWarning("El webhook de alertas respondió HTTP {Codigo}.", (int)resp.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning("No se pudo enviar la alerta al webhook: {Error}", ex.Message);
        }
    }
}
