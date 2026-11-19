using System.Globalization;

namespace TipoCambioDof;

public enum TipoComando { Diario, Historico, CrearTabla, Ayuda }

public sealed record Comando(TipoComando Tipo, DateOnly? Desde = null, DateOnly? Hasta = null)
{
    public const string Ayuda = """
        Uso: TipoCambioDof [comando] [opciones]

        Comandos:
          diario                 (por defecto) Sincroniza hasta lo publicado hoy en el DOF (SF60653 [hoy-10, hoy+1]) y verifica que exista.
          historico [--desde yyyy-MM-dd] [--hasta yyyy-MM-dd]
                                 Carga histórica por bloques de un año. Fechas de aplicación (SF60653). Por defecto desde 1991-11-14 hasta hoy+1 (publicación DOF de hoy); nunca pasa de hoy+DIAS_ADELANTE.
          crear-tabla            Crea dbo.tipo_cambio y dbo.tipo_cambio_auditoria si no existen.
          ayuda                  Muestra este mensaje.

        Variables de entorno:
          BANXICO_TOKEN          Token SIE de 64 caracteres (requerido para diario e historico).
          SQL_CONNECTION_STRING  Cadena de conexión a SQL Server (requerida).
          ALERT_WEBHOOK_URL      Opcional. Webhook (Slack/Teams) que recibe {"text": "..."} por cada alerta.
          DIAS_ATRAS=10  DIAS_ADELANTE=1  VALOR_MINIMO=10  VALOR_MAXIMO=40  VARIACION_MAXIMA_PCT=5
          REINTENTO_MINUTOS=30  HORA_LIMITE=16:00  PAUSA_HISTORICO_MS=1500
          ACTUALIZAR_LEGADOS     true/false (default false). Al terminar diario escribe lo publicado hoy en el DOF
                                 en BD_METRICS.dbo.FIN_MovtoMoeda y BD_LITO.dbo.Mon (requiere BD_METRICS y BD_LITO).

        Códigos de salida: 0 = OK, 1 = error, 2 = terminó con alertas, 64 = argumentos inválidos.
        """;

    /// <summary>Devuelve null si los argumentos no son válidos.</summary>
    public static Comando? Parsear(string[] args)
    {
        if (args.Length == 0)
            return new Comando(TipoComando.Diario);

        switch (args[0].ToLowerInvariant())
        {
            case "diario" when args.Length == 1:
                return new Comando(TipoComando.Diario);
            case "crear-tabla" when args.Length == 1:
                return new Comando(TipoComando.CrearTabla);
            case "ayuda" or "-h" or "--help" or "help":
                return new Comando(TipoComando.Ayuda);
            case "historico" or "histórico":
                DateOnly? desde = null, hasta = null;
                for (var i = 1; i < args.Length; i += 2)
                {
                    if (i + 1 >= args.Length || !TryFecha(args[i + 1], out var f))
                        return null;
                    switch (args[i])
                    {
                        case "--desde": desde = f; break;
                        case "--hasta": hasta = f; break;
                        default: return null;
                    }
                }
                return new Comando(TipoComando.Historico, desde, hasta);
            default:
                return null;
        }
    }

    private static bool TryFecha(string s, out DateOnly f) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out f);
}
