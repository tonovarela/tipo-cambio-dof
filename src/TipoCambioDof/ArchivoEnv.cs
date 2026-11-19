namespace TipoCambioDof;

/// <summary>
/// Para desarrollo local: carga el archivo <c>.env</c> del proyecto como variables de entorno, sin
/// sobrescribir las que ya existan. Dentro de un contenedor no hace nada (la imagen no incluye
/// <c>.env</c> y en producción la configuración llega por <c>docker run -e</c> / secretos).
/// </summary>
internal static class ArchivoEnv
{
    public static string? Cargar()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
            return null;

        var ruta = Buscar(Directory.GetCurrentDirectory()) ?? Buscar(AppContext.BaseDirectory);
        if (ruta is null)
            return null;

        foreach (var (clave, valor) in Parsear(File.ReadAllLines(ruta)))
            if (Environment.GetEnvironmentVariable(clave) is null)
                Environment.SetEnvironmentVariable(clave, valor);

        return ruta;
    }

    /// <summary>Formato KEY=valor; ignora líneas vacías y comentarios; separa solo en el primer '='.</summary>
    internal static IEnumerable<(string Clave, string Valor)> Parsear(IEnumerable<string> lineas)
    {
        foreach (var cruda in lineas)
        {
            var linea = cruda.Trim();
            if (linea.Length == 0 || linea.StartsWith('#'))
                continue;
            if (linea.StartsWith("export "))
                linea = linea["export ".Length..].TrimStart();

            var i = linea.IndexOf('=');
            if (i <= 0)
                continue;

            var clave = linea[..i].Trim();
            var valor = linea[(i + 1)..].Trim();
            if (valor.Length >= 2 && (valor[0] == '"' || valor[0] == '\'') && valor[^1] == valor[0])
                valor = valor[1..^1];

            if (valor.Length > 0)
                yield return (clave, valor);
        }
    }

    // Sube desde el directorio dado hasta encontrar .env (bin/Debug/net10.0 → raíz del repo).
    private static string? Buscar(string desde)
    {
        for (var dir = new DirectoryInfo(desde); dir is not null; dir = dir.Parent)
        {
            var ruta = Path.Combine(dir.FullName, ".env");
            if (File.Exists(ruta))
                return ruta;
        }
        return null;
    }
}
