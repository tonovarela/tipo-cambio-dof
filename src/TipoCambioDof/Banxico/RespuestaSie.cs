using System.Globalization;
using System.Text.Json;
using TipoCambioDof.Dominio;

namespace TipoCambioDof.Banxico;

public sealed record ResultadoSie(IReadOnlyList<DatoSerie> Datos, IReadOnlyList<string> Descartados);

/// <summary>
/// Lee la respuesta JSON de la API SIE. Particularidades (README §4):
/// <c>fecha</c> llega como dd/MM/yyyy, <c>dato</c> llega como texto (puede ser "N/E"),
/// y un rango sin datos llega sin la propiedad <c>datos</c>.
/// </summary>
internal static class RespuestaSie
{
    public static ResultadoSie Parsear(string json, string idSerie)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("bmx", out var bmx) ||
            !bmx.TryGetProperty("series", out var series) ||
            series.ValueKind != JsonValueKind.Array)
            throw new BanxicoException("La respuesta no contiene bmx.series.");

        var serie = series.EnumerateArray()
            .FirstOrDefault(s => s.TryGetProperty("idSerie", out var id) && id.GetString() == idSerie);
        if (serie.ValueKind == JsonValueKind.Undefined)
            throw new BanxicoException($"La respuesta no contiene la serie {idSerie}.");

        var datos = new List<DatoSerie>();
        var descartados = new List<string>();

        if (!serie.TryGetProperty("datos", out var lista) || lista.ValueKind != JsonValueKind.Array)
            return new ResultadoSie(datos, descartados);

        foreach (var item in lista.EnumerateArray())
        {
            var fechaTxt = Cadena(item, "fecha");
            var datoTxt = Cadena(item, "dato");

            if (!DateOnly.TryParseExact(fechaTxt, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var fecha))
            {
                descartados.Add($"fecha inválida '{fechaTxt}' (dato '{datoTxt}')");
                continue;
            }

            if (!decimal.TryParse(datoTxt, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var valor))
            {
                descartados.Add($"{fecha:yyyy-MM-dd}: dato no numérico '{datoTxt}'");
                continue;
            }

            datos.Add(new DatoSerie(fecha, valor));
        }

        return new ResultadoSie(datos, descartados);
    }

    /// <summary>Extrae <c>error.mensaje</c> / <c>error.detalle</c> de una respuesta de error, o un extracto del cuerpo.</summary>
    public static string MensajeError(string cuerpo)
    {
        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var mensaje = Cadena(error, "mensaje");
                var detalle = Cadena(error, "detalle");
                return string.IsNullOrEmpty(detalle) ? mensaje ?? "(sin mensaje)" : $"{mensaje} ({detalle})";
            }
        }
        catch (JsonException)
        {
        }

        return cuerpo.Length > 200 ? cuerpo[..200] + "…" : cuerpo;
    }

    private static string? Cadena(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
