using Microsoft.Extensions.Configuration;

namespace TipoCambioDof.Tests;

public class ConfiguracionTests
{
    private static Configuracion Cfg(params (string Clave, string Valor)[] valores) =>
        Configuracion.Desde(new ConfigurationBuilder()
            .AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Clave, v.Valor)))
            .Build());

    private static readonly (string, string)[] LegadosOk =
        [("ACTUALIZAR_LEGADOS", "true"), ("BD_METRICS", "MetricsBETA"), ("BD_LITO", "Lito_jess")];

    [Fact]
    public void Legados_apagado_por_defecto()
    {
        var cfg = Cfg();

        Assert.False(cfg.ActualizarLegados);
        Assert.Null(cfg.BdMetrics);
        Assert.Null(cfg.BdLito);
    }

    [Fact]
    public void Legados_lee_bandera_y_nombres()
    {
        var cfg = Cfg(LegadosOk);

        Assert.True(cfg.ActualizarLegados);
        Assert.Equal("MetricsBETA", cfg.BdMetrics);
        Assert.Equal("Lito_jess", cfg.BdLito);
        cfg.ExigirLegados();
    }

    [Fact]
    public void Bandera_no_booleana_es_error()
    {
        Assert.Throws<ConfiguracionInvalidaException>(() => Cfg(("ACTUALIZAR_LEGADOS", "si")));
    }

    [Theory]
    [InlineData("BD_METRICS")]
    [InlineData("BD_LITO")]
    public void Falta_nombre_de_bd(string faltante)
    {
        var cfg = Cfg(LegadosOk.Where(v => v.Item1 != faltante).ToArray());

        var ex = Assert.Throws<ConfiguracionInvalidaException>(cfg.ExigirLegados);
        Assert.Contains(faltante, ex.Message);
    }

    [Theory]
    [InlineData("x];DROP")]
    [InlineData("1Metrics")]
    [InlineData("Metrics BETA")]
    [InlineData("Metrics.dbo")]
    public void Nombre_de_bd_invalido(string nombre)
    {
        var cfg = Cfg([.. LegadosOk.Where(v => v.Item1 != "BD_LITO"), ("BD_LITO", nombre)]);

        var ex = Assert.Throws<ConfiguracionInvalidaException>(cfg.ExigirLegados);
        Assert.Contains("BD_LITO", ex.Message);
    }

    [Fact]
    public void Legados_requiere_dias_adelante_al_menos_uno()
    {
        var cfg = Cfg([.. LegadosOk, ("DIAS_ADELANTE", "0")]);

        var ex = Assert.Throws<ConfiguracionInvalidaException>(cfg.ExigirLegados);
        Assert.Contains("DIAS_ADELANTE", ex.Message);
    }
}
