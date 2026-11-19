using System.Globalization;
using Microsoft.Extensions.Logging;
using TipoCambioDof.Alertas;
using TipoCambioDof.Banxico;
using TipoCambioDof.Datos;
using TipoCambioDof.Dominio;

namespace TipoCambioDof.Procesos;

/// <summary>Consulta → validación → upsert de un rango de fechas (README §5 pasos 2 a 4).</summary>
public sealed class Sincronizador(
    BanxicoClient banxico,
    RepositorioTipoCambio repo,
    Alertador alertador,
    Configuracion cfg,
    ILogger<Sincronizador> log)
{
    public async Task SincronizarAsync(DateOnly inicio, DateOnly fin, CancellationToken ct)
    {
        // Regla D + 1: el valor publicado en el DOF el día D tiene fecha de aplicación D + 1 en SF60653.
        log.LogInformation(
            "Consultando {Serie} del {Inicio:yyyy-MM-dd} al {Fin:yyyy-MM-dd} (publicaciones DOF del {DofInicio:yyyy-MM-dd} al {DofFin:yyyy-MM-dd}).",
            BanxicoClient.Serie, inicio, fin, inicio.AddDays(-1), fin.AddDays(-1));

        var respuesta = await banxico.ObtenerAsync(inicio, fin, ct);

        foreach (var d in respuesta.Descartados)
            log.LogWarning("Dato omitido: {Detalle}", d);

        if (respuesta.Datos.Count == 0)
        {
            log.LogInformation("Banxico no devolvió datos para el rango.");
            return;
        }

        var primeraFecha = respuesta.Datos.Min(d => d.Fecha);
        var previo = await repo.UltimoValorAntesDeAsync(primeraFecha, ct);
        var validacion = Validador.Validar(respuesta.Datos, previo, cfg);

        foreach (var d in validacion.FueraDeRango)
            await alertador.EnviarAsync(
                $"Valor fuera de rango ({F(cfg.ValorMinimo)}–{F(cfg.ValorMaximo)}) para {d.Fecha:yyyy-MM-dd}: {F(d.Valor)}. No se guardó.",
                ct);

        foreach (var v in validacion.Variaciones)
            await alertador.EnviarAsync(
                $"Variación de {F(v.Porcentaje)} % el {v.Dato.Fecha:yyyy-MM-dd}: {F(v.ValorAnterior)} → {F(v.Dato.Valor)}. Se guardó.",
                ct);

        if (validacion.Aceptados.Count == 0)
            return;

        var resumen = await repo.UpsertAsync(validacion.Aceptados, ct);

        foreach (var c in resumen.Actualizados)
            log.LogWarning("Banxico revisó el valor del {Fecha:yyyy-MM-dd}: {Anterior} → {Nuevo} (registrado en auditoría).",
                c.Fecha, F(c.ValorAnterior), F(c.ValorNuevo));

        log.LogInformation(
            "Rango {Inicio:yyyy-MM-dd}…{Fin:yyyy-MM-dd}: {Insertados} insertados, {Actualizados} actualizados, {SinCambio} sin cambio.",
            primeraFecha, respuesta.Datos.Max(d => d.Fecha),
            resumen.Insertados, resumen.Actualizados.Count, resumen.SinCambio);
    }

    private static string F(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
