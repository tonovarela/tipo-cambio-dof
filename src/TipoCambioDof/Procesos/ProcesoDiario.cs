using Microsoft.Extensions.Logging;
using TipoCambioDof.Alertas;
using TipoCambioDof.Datos;

namespace TipoCambioDof.Procesos;

/// <summary>
/// Corrida diaria (README §5). Sincroniza [hoy − DIAS_ATRAS, hoy + DIAS_ADELANTE] en fechas de aplicación;
/// con el default (1) eso llega hasta lo publicado hoy en el DOF, que en SF60653 tiene fecha hoy + 1.
/// Verifica que exista el valor de la fecha de cobertura; si no, reintenta cada REINTENTO_MINUTOS
/// mientras no pase de HORA_LIMITE y después alerta. Pensado para lanzarse L–V a las 13:00 (hora CDMX)
/// desde un programador externo.
/// </summary>
public sealed class ProcesoDiario(
    Sincronizador sincronizador,
    RepositorioTipoCambio repo,
    Alertador alertador,
    Reloj reloj,
    Configuracion cfg,
    ILogger<ProcesoDiario> log)
{
    /// <summary>
    /// Fecha que debe existir al terminar: mañana si el rango incluye días futuros, hoy si termina hoy
    /// (no tiene sentido exigir una fecha que no se consultó).
    /// </summary>
    internal static DateOnly FechaCobertura(DateOnly hoy, int diasAdelante) => hoy.AddDays(Math.Min(1, diasAdelante));

    public async Task EjecutarAsync(CancellationToken ct)
    {
        while (true)
        {
            var ahora = reloj.Ahora();
            var hoy = DateOnly.FromDateTime(ahora);
            var objetivo = FechaCobertura(hoy, cfg.DiasAdelante);

            await sincronizador.SincronizarAsync(hoy.AddDays(-cfg.DiasAtras), hoy.AddDays(cfg.DiasAdelante), ct);

            if (await repo.ExisteAsync(objetivo, ct))
            {
                log.LogInformation(
                    "Cobertura OK: existe el tipo de cambio con fecha de aplicación {Objetivo:yyyy-MM-dd} (publicación DOF {Dof:yyyy-MM-dd}).",
                    objetivo, objetivo.AddDays(-1));
                return;
            }

            var siguiente = reloj.Ahora() + cfg.IntervaloReintento;
            if (DateOnly.FromDateTime(siguiente) != hoy || TimeOnly.FromDateTime(siguiente) > cfg.HoraLimite)
            {
                await alertador.EnviarAsync(
                    $"Sin tipo de cambio con fecha de aplicación {objetivo:yyyy-MM-dd} (publicación DOF {objetivo.AddDays(-1):yyyy-MM-dd}) a las {reloj.Ahora():HH:mm} (hora CDMX).", ct);
                return;
            }

            log.LogWarning("Aún no existe el valor para {Objetivo:yyyy-MM-dd}. Reintento a las {Siguiente:HH:mm}.",
                objetivo, siguiente);
            await Task.Delay(cfg.IntervaloReintento, reloj.Tiempo, ct);
        }
    }
}
