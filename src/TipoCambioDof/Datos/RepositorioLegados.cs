using System.Data;
using Microsoft.Data.SqlClient;

namespace TipoCambioDof.Datos;

/// <summary>Resultado de <c>Actualizacion.sql</c>: movto ∈ INSERT|UPDATE|SIN_CAMBIO, mon ∈ UPDATE|NO_EXISTE (SPEC 02: Mon se actualiza en cada corrida).</summary>
public sealed record ResultadoLegados(string Movto, string Mon)
{
    public const string NoExiste = "NO_EXISTE";
}

/// <summary>
/// Escribe en los sistemas legados (Metrics y Lito) con <c>Datos/Actualizacion.sql</c> (SPEC 01).
/// Usa la misma SQL_CONNECTION_STRING con nombres de tres partes y una transacción propia,
/// separada del upsert de dbo.tipo_cambio.
/// </summary>
public sealed class RepositorioLegados(Configuracion cfg)
{
    internal const string Recurso = "Actualizacion.sql";
    internal const string MarcadorMetrics = "{BD_METRICS}";
    internal const string MarcadorLito = "{BD_LITO}";

    /// <summary>
    /// Reemplaza los marcadores por los nombres de BD entre corchetes. Los nombres ya vienen validados por
    /// <see cref="Configuracion.ExigirLegados"/>: un identificador no se puede pasar como parámetro.
    /// </summary>
    internal static string PrepararSql(string plantilla, string bdMetrics, string bdLito) =>
        plantilla.Replace(MarcadorMetrics, $"[{bdMetrics}]").Replace(MarcadorLito, $"[{bdLito}]");

    internal static string LeerPlantilla()
    {
        using var stream = typeof(RepositorioLegados).Assembly.GetManifestResourceStream(Recurso)
                           ?? throw new InvalidOperationException($"No se encontró el recurso {Recurso}.");
        using var lector = new StreamReader(stream);
        return lector.ReadToEnd();
    }

    public async Task<ResultadoLegados> ActualizarAsync(DateOnly fechaPublicacionDof, decimal valor, CancellationToken ct)
    {
        var sql = PrepararSql(LeerPlantilla(), cfg.BdMetrics!, cfg.BdLito!);

        await using var cn = new SqlConnection(cfg.CadenaConexion);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        ResultadoLegados resultado;
        await using (var cmd = new SqlCommand(sql, cn, tx))
        {
            cmd.Parameters.Add(new SqlParameter("@valor", SqlDbType.Decimal) { Precision = 12, Scale = 6, Value = valor });
            cmd.Parameters.Add(new SqlParameter("@fecha", SqlDbType.Date) { Value = fechaPublicacionDof.ToDateTime(TimeOnly.MinValue) });

            await using var rd = await cmd.ExecuteReaderAsync(ct);
            if (!await rd.ReadAsync(ct))
                throw new InvalidOperationException($"{Recurso} no devolvió el resultado esperado.");
            resultado = new ResultadoLegados(rd.GetString(0), rd.GetString(1));
        }

        await tx.CommitAsync(ct);
        return resultado;
    }
}
