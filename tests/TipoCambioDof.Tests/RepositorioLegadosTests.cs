using TipoCambioDof.Datos;

namespace TipoCambioDof.Tests;

public class RepositorioLegadosTests
{
    [Fact]
    public void El_recurso_embebido_existe_y_tiene_los_marcadores_y_parametros()
    {
        var plantilla = RepositorioLegados.LeerPlantilla();

        Assert.Contains(RepositorioLegados.MarcadorMetrics, plantilla);
        Assert.Contains(RepositorioLegados.MarcadorLito, plantilla);
        Assert.Contains("@valor", plantilla);
        Assert.Contains("@fecha", plantilla);
        Assert.DoesNotMatch(@"(?im)^\s*GO\s*$", plantilla);
    }

    [Fact]
    public void Mon_se_actualiza_en_cada_corrida_sin_condicion_de_fecha()
    {
        var plantilla = RepositorioLegados.LeerPlantilla();
        var bloqueMon = plantilla[plantilla.IndexOf("IF NOT EXISTS (SELECT 1 FROM {BD_LITO}.dbo.Mon", StringComparison.Ordinal)..];

        Assert.DoesNotContain("CAST(UltimoCambio AS DATE) < @fecha", bloqueMon);
        Assert.DoesNotContain("'SIN_CAMBIO'", bloqueMon);
        Assert.Contains("UltimoCambio = GETDATE()", bloqueMon);
    }

    [Fact]
    public void PrepararSql_reemplaza_ambos_marcadores_entre_corchetes()
    {
        var sql = RepositorioLegados.PrepararSql(RepositorioLegados.LeerPlantilla(), "MetricsBETA", "Lito_jess");

        Assert.DoesNotContain("{BD_", sql);
        Assert.Contains("[MetricsBETA].dbo.FIN_MovtoMoeda", sql);
        Assert.Contains("[Lito_jess].dbo.Mon", sql);
    }
}
