namespace TipoCambioDof.Tests;

public class ArchivoEnvTests
{
    [Fact]
    public void Parsea_claves_ignorando_comentarios_vacios_y_comillas()
    {
        string[] lineas =
        [
            "# comentario",
            "",
            "BANXICO_TOKEN=abc",
            "SQL_CONNECTION_STRING=Server=localhost,1433;Database=db;Password=a=b;TrustServerCertificate=True",
            "ALERT_WEBHOOK_URL=",
            "export HORA_LIMITE=\"16:30\"",
            "sin_igual",
        ];

        var r = ArchivoEnv.Parsear(lineas).ToList();

        Assert.Equal(
            [
                ("BANXICO_TOKEN", "abc"),
                ("SQL_CONNECTION_STRING", "Server=localhost,1433;Database=db;Password=a=b;TrustServerCertificate=True"),
                ("HORA_LIMITE", "16:30"),
            ],
            r);
    }
}
