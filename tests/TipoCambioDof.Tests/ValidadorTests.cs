using TipoCambioDof.Dominio;
using TipoCambioDof.Procesos;

namespace TipoCambioDof.Tests;

public class ValidadorTests
{
    private static readonly Configuracion Cfg = new();
    private static DatoSerie D(int dia, decimal v) => new(new DateOnly(2026, 11, dia), v);

    [Fact]
    public void Descarta_valores_fuera_de_rango_sin_afectar_la_variacion()
    {
        var r = Validador.Validar([D(19, 18.40m), D(20, 0.5m), D(21, 18.50m)], null, Cfg);

        Assert.Equal([D(19, 18.40m), D(21, 18.50m)], r.Aceptados);
        Assert.Equal([D(20, 0.5m)], r.FueraDeRango);
        Assert.Empty(r.Variaciones);
    }

    [Fact]
    public void Variacion_mayor_al_limite_se_alerta_pero_se_acepta()
    {
        var r = Validador.Validar([D(20, 19.50m)], valorPrevio: 18.00m, Cfg);

        Assert.Equal([D(20, 19.50m)], r.Aceptados);
        var v = Assert.Single(r.Variaciones);
        Assert.Equal(8.33m, v.Porcentaje);
        Assert.Equal(18.00m, v.ValorAnterior);
    }

    [Fact]
    public void Compara_contra_el_dato_anterior_ordenado_por_fecha()
    {
        // Llegan desordenados; 18.6188 → 18.5525 → 18.4677 no rebasa el 5 %.
        var r = Validador.Validar([D(19, 18.4677m), D(17, 18.6188m), D(18, 18.5525m)], null, Cfg);

        Assert.Equal([17, 18, 19], r.Aceptados.Select(a => a.Fecha.Day));
        Assert.Empty(r.Variaciones);
    }
}

public class BloquesHistoricoTests
{
    [Fact]
    public void Parte_el_rango_por_anio_calendario()
    {
        var b = ProcesoHistorico.Bloques(new DateOnly(1991, 11, 14), new DateOnly(1993, 3, 1)).ToList();

        Assert.Equal(
            [
                (new DateOnly(1991, 11, 14), new DateOnly(1991, 12, 31)),
                (new DateOnly(1992, 1, 1), new DateOnly(1992, 12, 31)),
                (new DateOnly(1993, 1, 1), new DateOnly(1993, 3, 1)),
            ],
            b);
    }

    [Theory]
    [InlineData(null, "2026-11-19")]          // sin --hasta: hasta hoy
    [InlineData("2026-11-10", "2026-11-10")]  // --hasta en el pasado se respeta
    [InlineData("2026-11-25", "2026-11-19")]  // --hasta en el futuro se recorta a hoy
    public void Fin_del_historico_no_pasa_del_limite(string? hasta, string esperado)
    {
        var limite = new DateOnly(2026, 11, 19);
        DateOnly? h = hasta is null ? null : DateOnly.Parse(hasta);

        Assert.Equal(DateOnly.Parse(esperado), ProcesoHistorico.FinEfectivo(h, limite));
    }

    [Theory]
    [InlineData(0, 19)]   // rango hasta hoy: se verifica hoy
    [InlineData(1, 20)]   // con días adelante: se verifica mañana
    [InlineData(5, 20)]
    public void Fecha_de_cobertura(int diasAdelante, int diaEsperado) =>
        Assert.Equal(new DateOnly(2026, 11, diaEsperado), ProcesoDiario.FechaCobertura(new DateOnly(2026, 11, 19), diasAdelante));

    [Fact]
    public void Por_defecto_cubre_hasta_la_publicacion_DOF_de_hoy()
    {
        var hoy = new DateOnly(2026, 11, 19);
        var cobertura = ProcesoDiario.FechaCobertura(hoy, new Configuracion().DiasAdelante);

        // Regla D + 1: la fila con fecha de aplicación hoy + 1 es la que el DOF publicó hoy.
        Assert.Equal(hoy.AddDays(1), cobertura);
        Assert.Equal(hoy, cobertura.AddDays(-1));
    }

    [Fact]
    public void Rango_de_un_dia_es_un_bloque()
    {
        var d = new DateOnly(2026, 11, 19);
        Assert.Equal([(d, d)], ProcesoHistorico.Bloques(d, d));
    }
}

public class ComandoTests
{
    [Fact]
    public void Sin_argumentos_es_diario() => Assert.Equal(TipoComando.Diario, Comando.Parsear([])!.Tipo);

    [Fact]
    public void Historico_con_fechas()
    {
        var c = Comando.Parsear(["historico", "--desde", "2020-01-01", "--hasta", "2020-12-31"])!;

        Assert.Equal(TipoComando.Historico, c.Tipo);
        Assert.Equal(new DateOnly(2020, 1, 1), c.Desde);
        Assert.Equal(new DateOnly(2020, 12, 31), c.Hasta);
    }

    [Theory]
    [InlineData("historico", "--desde")]
    [InlineData("historico", "--desde", "01/01/2020")]
    [InlineData("historico", "--otro", "2020-01-01")]
    [InlineData("diario", "extra")]
    [InlineData("desconocido")]
    public void Argumentos_invalidos(params string[] args) => Assert.Null(Comando.Parsear(args));
}
