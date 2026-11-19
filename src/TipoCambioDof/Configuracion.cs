using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TipoCambioDof;

/// <summary>
/// Configuración leída de variables de entorno (nombres planos, pensados para <c>docker run -e</c>).
/// Los valores por defecto siguen el README (secciones 5 y 8).
/// </summary>
public sealed record Configuracion
{
    public string? TokenBanxico { get; init; }
    public string? CadenaConexion { get; init; }
    public string? WebhookAlertas { get; init; }

    public int DiasAtras { get; init; } = 10;
    public int DiasAdelante { get; init; } = 1;
    public decimal ValorMinimo { get; init; } = 10.0m;
    public decimal ValorMaximo { get; init; } = 40.0m;
    public decimal VariacionMaximaPct { get; init; } = 5.0m;
    public TimeSpan IntervaloReintento { get; init; } = TimeSpan.FromMinutes(30);
    public TimeOnly HoraLimite { get; init; } = new(16, 0);
    public TimeSpan PausaHistorico { get; init; } = TimeSpan.FromMilliseconds(1500);
    public string ZonaHoraria { get; init; } = "America/Mexico_City";

    public static Configuracion Desde(IConfiguration c) => new()
    {
        TokenBanxico = Texto(c, "BANXICO_TOKEN"),
        CadenaConexion = Texto(c, "SQL_CONNECTION_STRING"),
        WebhookAlertas = Texto(c, "ALERT_WEBHOOK_URL"),
        DiasAtras = Entero(c, "DIAS_ATRAS", 10),
        DiasAdelante = Entero(c, "DIAS_ADELANTE", 1),
        ValorMinimo = Decimal(c, "VALOR_MINIMO", 10.0m),
        ValorMaximo = Decimal(c, "VALOR_MAXIMO", 40.0m),
        VariacionMaximaPct = Decimal(c, "VARIACION_MAXIMA_PCT", 5.0m),
        IntervaloReintento = TimeSpan.FromMinutes(Entero(c, "REINTENTO_MINUTOS", 30)),
        HoraLimite = Texto(c, "HORA_LIMITE") is { } h
            ? TimeOnly.ParseExact(h, "HH:mm", CultureInfo.InvariantCulture)
            : new TimeOnly(16, 0),
        PausaHistorico = TimeSpan.FromMilliseconds(Entero(c, "PAUSA_HISTORICO_MS", 1500)),
    };

    public void ExigirCadenaConexion()
    {
        if (string.IsNullOrWhiteSpace(CadenaConexion))
            throw new ConfiguracionInvalidaException("Falta la variable de entorno SQL_CONNECTION_STRING.");
    }

    public void ExigirToken()
    {
        if (string.IsNullOrWhiteSpace(TokenBanxico))
            throw new ConfiguracionInvalidaException("Falta la variable de entorno BANXICO_TOKEN.");
        if (TokenBanxico.Length != 64)
            throw new ConfiguracionInvalidaException(
                $"BANXICO_TOKEN debe tener 64 caracteres (tiene {TokenBanxico.Length}).");
    }

    private static string? Texto(IConfiguration c, string clave) =>
        string.IsNullOrWhiteSpace(c[clave]) ? null : c[clave]!.Trim();

    private static int Entero(IConfiguration c, string clave, int defecto) =>
        Texto(c, clave) is { } v
            ? int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0
                ? n
                : throw new ConfiguracionInvalidaException($"{clave} debe ser un entero no negativo: '{v}'.")
            : defecto;

    private static decimal Decimal(IConfiguration c, string clave, decimal defecto) =>
        Texto(c, clave) is { } v
            ? decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)
                ? n
                : throw new ConfiguracionInvalidaException($"{clave} debe ser un número: '{v}'.")
            : defecto;
}

public sealed class ConfiguracionInvalidaException(string mensaje) : Exception(mensaje);
