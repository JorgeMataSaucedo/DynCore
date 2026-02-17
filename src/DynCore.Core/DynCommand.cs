using System.Text.Json.Serialization;

namespace DynCore.Core;

/// <summary>
/// Definición de un comando DynCore. Reemplaza a acsComandoSQL.
/// Se carga desde archivos JSON versionados en Git.
/// </summary>
public class DynCommand
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("procedure")]
    public string Procedure { get; set; } = string.Empty;

    [JsonPropertyName("connection")]
    public string Connection { get; set; } = string.Empty;

    [JsonPropertyName("strategy")]
    public string Strategy { get; set; } = "Query";

    [JsonPropertyName("params")]
    public List<DynParam> Params { get; set; } = new();

    /// <summary>
    /// IDs de comandos lookup a ejecutar junto con el principal.
    /// Reemplaza WCS tipo 3/4 (combos).
    /// Ejemplo: ["rh.lookups.puestos", "rh.lookups.areas"]
    /// </summary>
    [JsonPropertyName("includes")]
    public List<string> Includes { get; set; } = new();

    /// <summary>
    /// Timeout en segundos para la ejecución del SP.
    /// Default: 30 segundos. SPs pesados pueden necesitar más.
    /// </summary>
    [JsonPropertyName("timeout")]
    public int Timeout { get; set; } = 30;

    /// <summary>
    /// Tiempo de caché en segundos. 0 = sin caché (default).
    /// Ideal para lookups/catálogos que no cambian frecuentemente.
    /// Ejemplo: "cache": 300 → cachea resultado 5 minutos.
    /// </summary>
    [JsonPropertyName("cache")]
    public int Cache { get; set; } = 0;
}

/// <summary>
/// Definición de un parámetro SQL. Tipado y validable.
/// Reemplaza el formato pipe: "@pdFechaIni|FechaIni||@pdFechaFin|FechaFin"
/// </summary>
public class DynParam
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("optional")]
    public bool Optional { get; set; } = false;
}
