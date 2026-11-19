namespace TipoCambioDof;

/// <summary>Hora de la Ciudad de México, que es la que define "hoy", la ventana de reintentos y la hora límite.</summary>
public sealed class Reloj(TimeProvider tiempo, Configuracion cfg)
{
    private readonly TimeZoneInfo _zona = ResolverZona(cfg.ZonaHoraria);

    public TimeProvider Tiempo => tiempo;

    public DateTime Ahora() => TimeZoneInfo.ConvertTime(tiempo.GetUtcNow(), _zona).DateTime;

    public DateOnly Hoy() => DateOnly.FromDateTime(Ahora());

    private static TimeZoneInfo ResolverZona(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            // Sin tzdata en la imagen: la CDMX no tiene horario de verano desde 2022, UTC−6 fijo es correcto.
            return TimeZoneInfo.CreateCustomTimeZone(id, TimeSpan.FromHours(-6), id, id);
        }
    }
}
