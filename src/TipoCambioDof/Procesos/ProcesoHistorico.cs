using Microsoft.Extensions.Logging;

namespace TipoCambioDof.Procesos;

/// <summary>Carga histórica por bloques de un año con pausa entre llamadas (README §8).</summary>
public sealed class ProcesoHistorico(
    Sincronizador sincronizador,
    Reloj reloj,
    Configuracion cfg,
    ILogger<ProcesoHistorico> log)
{
    public static readonly DateOnly InicioSerie = new(1991, 11, 14);

    public async Task EjecutarAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct)
    {
        var inicio = desde ?? InicioSerie;
        var limite = reloj.Hoy().AddDays(cfg.DiasAdelante);
        var fin = FinEfectivo(hasta, limite);
        if (hasta > limite)
            log.LogWarning("--hasta {Hasta:yyyy-MM-dd} es posterior al límite {Limite:yyyy-MM-dd}; se usa el límite.",
                hasta, limite);
        if (inicio > fin)
            throw new ConfiguracionInvalidaException($"--desde ({inicio:yyyy-MM-dd}) es posterior a --hasta ({fin:yyyy-MM-dd}).");

        var bloques = Bloques(inicio, fin).ToList();
        log.LogInformation("Carga histórica {Inicio:yyyy-MM-dd}…{Fin:yyyy-MM-dd} en {N} bloques.", inicio, fin, bloques.Count);

        for (var i = 0; i < bloques.Count; i++)
        {
            if (i > 0)
                await Task.Delay(cfg.PausaHistorico, reloj.Tiempo, ct);
            await sincronizador.SincronizarAsync(bloques[i].Inicio, bloques[i].Fin, ct);
        }
    }

    /// <summary>Fin del rango: <paramref name="hasta"/> si se dio, pero nunca después de hoy + DIAS_ADELANTE.</summary>
    internal static DateOnly FinEfectivo(DateOnly? hasta, DateOnly limite) =>
        hasta is { } h && h < limite ? h : limite;

    /// <summary>Parte [inicio, fin] en bloques por año calendario.</summary>
    internal static IEnumerable<(DateOnly Inicio, DateOnly Fin)> Bloques(DateOnly inicio, DateOnly fin)
    {
        for (var a = inicio; a <= fin;)
        {
            var finAnio = new DateOnly(a.Year, 12, 31);
            var b = finAnio < fin ? finAnio : fin;
            yield return (a, b);
            a = b.AddDays(1);
        }
    }
}
