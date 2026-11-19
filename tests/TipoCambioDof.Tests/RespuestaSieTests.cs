using TipoCambioDof.Banxico;
using TipoCambioDof.Dominio;

namespace TipoCambioDof.Tests;

public class RespuestaSieTests
{
    [Fact]
    public void Lee_fechas_ddMMyyyy_y_datos_como_texto()
    {
        const string json = """
            {"bmx":{"series":[{"idSerie":"SF60653","titulo":"x","datos":[
              {"fecha":"19/11/2026","dato":"18.4677"},
              {"fecha":"20/11/2026","dato":"18.6723"}]}]}}
            """;

        var r = RespuestaSie.Parsear(json, "SF60653");

        Assert.Equal(
            [new DatoSerie(new DateOnly(2026, 11, 19), 18.4677m), new DatoSerie(new DateOnly(2026, 11, 20), 18.6723m)],
            r.Datos);
        Assert.Empty(r.Descartados);
    }

    [Fact]
    public void Rango_sin_datos_llega_sin_la_propiedad_datos()
    {
        const string json = """{"bmx":{"series":[{"idSerie":"SF60653","titulo":"x"}]}}""";

        var r = RespuestaSie.Parsear(json, "SF60653");

        Assert.Empty(r.Datos);
    }

    [Theory]
    [InlineData("N/E")]
    [InlineData("")]
    [InlineData("1,234.5")]
    public void Omite_datos_no_numericos(string dato)
    {
        var json = $$$"""{"bmx":{"series":[{"idSerie":"SF60653","datos":[{"fecha":"19/11/2026","dato":"{{{dato}}}"}]}]}}""";

        var r = RespuestaSie.Parsear(json, "SF60653");

        Assert.Empty(r.Datos);
        Assert.Single(r.Descartados);
    }

    [Fact]
    public void Omite_fechas_en_formato_inesperado()
    {
        const string json = """{"bmx":{"series":[{"idSerie":"SF60653","datos":[{"fecha":"2026-11-19","dato":"18.4"}]}]}}""";

        Assert.Single(RespuestaSie.Parsear(json, "SF60653").Descartados);
    }

    [Fact]
    public void Serie_ausente_es_error()
    {
        const string json = """{"bmx":{"series":[{"idSerie":"SF43718","datos":[]}]}}""";

        Assert.Throws<BanxicoException>(() => RespuestaSie.Parsear(json, "SF60653"));
    }

    [Fact]
    public void Extrae_mensaje_de_error()
    {
        const string json = """{"error":{"mensaje":"Token inválido","detalle":"El token no es válido"}}""";

        Assert.Equal("Token inválido (El token no es válido)", RespuestaSie.MensajeError(json));
    }
}
