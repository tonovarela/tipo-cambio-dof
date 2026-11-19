using System.Data;
using Microsoft.Data.SqlClient;
using TipoCambioDof.Dominio;

namespace TipoCambioDof.Datos;

public sealed record CambioValor(DateOnly Fecha, decimal ValorAnterior, decimal ValorNuevo);

public sealed record ResumenUpsert(int Insertados, IReadOnlyList<CambioValor> Actualizados, int SinCambio);

/// <summary>Acceso a dbo.tipo_cambio en SQL Server.</summary>
public sealed class RepositorioTipoCambio(Configuracion cfg)
{
    public const string Moneda = "USD";
    public const string Fuente = "BANXICO_SF60653";

    private const string SqlMerge = """
        MERGE dbo.tipo_cambio WITH (HOLDLOCK) AS t
        USING (SELECT @fecha AS fecha_aplicacion, @moneda AS moneda) AS s
           ON t.fecha_aplicacion = s.fecha_aplicacion AND t.moneda = s.moneda
        WHEN MATCHED AND t.valor <> @valor THEN
            UPDATE SET valor = @valor, fecha_actualizacion = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT (fecha_aplicacion, moneda, valor, fecha_publicacion_dof, fuente, fecha_captura)
            VALUES (@fecha, @moneda, @valor, DATEADD(day, -1, @fecha), @fuente, SYSUTCDATETIME())
        OUTPUT $action, deleted.valor;
        """;

    private const string SqlAuditoria = """
        INSERT INTO dbo.tipo_cambio_auditoria (fecha_aplicacion, moneda, valor_anterior, valor_nuevo)
        VALUES (@fecha, @moneda, @anterior, @valor);
        """;

    public async Task CrearEsquemaAsync(CancellationToken ct)
    {
        await using var stream = typeof(RepositorioTipoCambio).Assembly.GetManifestResourceStream("Esquema.sql")
                                 ?? throw new InvalidOperationException("No se encontró el recurso Esquema.sql.");
        using var lector = new StreamReader(stream);
        var sql = await lector.ReadToEndAsync(ct);

        await using var cn = await AbrirAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Upsert idempotente (README §5 paso 4). Los cambios de valor quedan en tipo_cambio_auditoria.</summary>
    public async Task<ResumenUpsert> UpsertAsync(IReadOnlyList<DatoSerie> datos, CancellationToken ct)
    {
        var insertados = 0;
        var sinCambio = 0;
        var actualizados = new List<CambioValor>();

        await using var cn = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);

        foreach (var dato in datos)
        {
            string? accion = null;
            decimal? anterior = null;

            await using (var cmd = new SqlCommand(SqlMerge, cn, tx))
            {
                AgregarParametros(cmd, dato);
                await using var rd = await cmd.ExecuteReaderAsync(ct);
                if (await rd.ReadAsync(ct))
                {
                    accion = rd.GetString(0);
                    anterior = rd.IsDBNull(1) ? null : rd.GetDecimal(1);
                }
            }

            switch (accion)
            {
                case "INSERT":
                    insertados++;
                    break;
                case "UPDATE":
                    actualizados.Add(new CambioValor(dato.Fecha, anterior!.Value, dato.Valor));
                    await using (var aud = new SqlCommand(SqlAuditoria, cn, tx))
                    {
                        AgregarParametros(aud, dato);
                        aud.Parameters.Add(Decimal("@anterior", anterior.Value));
                        await aud.ExecuteNonQueryAsync(ct);
                    }
                    break;
                default:
                    sinCambio++;
                    break;
            }
        }

        await tx.CommitAsync(ct);
        return new ResumenUpsert(insertados, actualizados, sinCambio);
    }

    /// <summary>Último valor guardado antes de <paramref name="fecha"/>, para calcular la variación del primer dato.</summary>
    public async Task<decimal?> UltimoValorAntesDeAsync(DateOnly fecha, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) valor FROM dbo.tipo_cambio
            WHERE moneda = @moneda AND fecha_aplicacion < @fecha
            ORDER BY fecha_aplicacion DESC;
            """;
        return await EscalarAsync(sql, fecha, ct);
    }

    /// <summary>Valor guardado para <paramref name="fechaAplicacion"/>, o null si no existe.</summary>
    public async Task<decimal?> ValorAsync(DateOnly fechaAplicacion, CancellationToken ct)
    {
        const string sql = "SELECT valor FROM dbo.tipo_cambio WHERE moneda = @moneda AND fecha_aplicacion = @fecha;";
        return await EscalarAsync(sql, fechaAplicacion, ct);
    }

    public async Task<bool> ExisteAsync(DateOnly fecha, CancellationToken ct) =>
        await ValorAsync(fecha, ct) is not null;

    private async Task<decimal?> EscalarAsync(string sql, DateOnly fecha, CancellationToken ct)
    {
        await using var cn = await AbrirAsync(ct);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.Add(new SqlParameter("@fecha", SqlDbType.Date) { Value = fecha.ToDateTime(TimeOnly.MinValue) });
        cmd.Parameters.Add(new SqlParameter("@moneda", SqlDbType.Char, 3) { Value = Moneda });
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : (decimal)r;
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var cn = new SqlConnection(cfg.CadenaConexion);
        await cn.OpenAsync(ct);
        return cn;
    }

    private static void AgregarParametros(SqlCommand cmd, DatoSerie dato)
    {
        cmd.Parameters.Add(new SqlParameter("@fecha", SqlDbType.Date) { Value = dato.Fecha.ToDateTime(TimeOnly.MinValue) });
        cmd.Parameters.Add(new SqlParameter("@moneda", SqlDbType.Char, 3) { Value = Moneda });
        cmd.Parameters.Add(new SqlParameter("@fuente", SqlDbType.VarChar, 20) { Value = Fuente });
        cmd.Parameters.Add(Decimal("@valor", dato.Valor));
    }

    private static SqlParameter Decimal(string nombre, decimal valor) =>
        new(nombre, SqlDbType.Decimal) { Precision = 12, Scale = 6, Value = valor };
}
