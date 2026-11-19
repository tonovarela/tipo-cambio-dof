namespace TipoCambioDof.Dominio;

/// <summary>Un valor de SF60653: <see cref="Fecha"/> es la fecha de aplicación (liquidación).</summary>
public sealed record DatoSerie(DateOnly Fecha, decimal Valor);

public sealed record Variacion(DatoSerie Dato, decimal ValorAnterior, decimal Porcentaje);

public sealed record ResultadoValidacion(
    IReadOnlyList<DatoSerie> Aceptados,
    IReadOnlyList<DatoSerie> FueraDeRango,
    IReadOnlyList<Variacion> Variaciones);

/// <summary>README §5 paso 3: rango razonable (se descarta) y variación diaria (se alerta pero se guarda).</summary>
public static class Validador
{
    public static ResultadoValidacion Validar(
        IEnumerable<DatoSerie> datos, decimal? valorPrevio, Configuracion cfg)
    {
        var aceptados = new List<DatoSerie>();
        var fueraDeRango = new List<DatoSerie>();
        var variaciones = new List<Variacion>();
        var anterior = valorPrevio;

        foreach (var dato in datos.OrderBy(d => d.Fecha))
        {
            if (dato.Valor < cfg.ValorMinimo || dato.Valor > cfg.ValorMaximo)
            {
                fueraDeRango.Add(dato);
                continue;
            }

            if (anterior is > 0m)
            {
                var pct = Math.Abs(dato.Valor - anterior.Value) / anterior.Value * 100m;
                if (pct > cfg.VariacionMaximaPct)
                    variaciones.Add(new Variacion(dato, anterior.Value, Math.Round(pct, 2)));
            }

            aceptados.Add(dato);
            anterior = dato.Valor;
        }

        return new ResultadoValidacion(aceptados, fueraDeRango, variaciones);
    }
}
