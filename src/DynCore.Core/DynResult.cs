namespace DynCore.Core;

/// <summary>
/// Resultado tipado de una ejecución DynCore.
/// NUNCA es null. Siempre tiene IsSuccess o Error.
/// Reemplaza el JObject crudo de WCS que podía ser null, vacío, o explotar.
/// </summary>
public class DynResult
{
    public bool IsSuccess { get; private set; }
    public string? Error { get; private set; }
    public string CommandId { get; set; } = string.Empty;
    public long ElapsedMs { get; set; }

    /// <summary>
    /// Datos para estrategias Query/Transaction (un solo resultado).
    /// Lista de filas donde cada fila es un diccionario columna→valor.
    /// </summary>
    public List<Dictionary<string, object?>> Data { get; private set; } = new();

    /// <summary>
    /// Datos para estrategia MultiResult (múltiples datasets).
    /// Key = "info", "info2", "info3"... igual que WCS pero sin JArray.Parse("null").
    /// </summary>
    public Dictionary<string, List<Dictionary<string, object?>>> DataSets { get; private set; } = new();

    /// <summary>
    /// Datos de lookups/catálogos cargados por "includes" (reemplaza WCS tipo 3/4 combos).
    /// Key = commandId del include, Value = datos del lookup.
    /// Ejemplo: result.Lookups["rh.lookups.puestos"] → lista de puestos
    /// </summary>
    public Dictionary<string, List<Dictionary<string, object?>>> Lookups { get; set; } = new();

    /// <summary>
    /// Acceso directo a un dataset por nombre: result["info"]
    /// </summary>
    public List<Dictionary<string, object?>> this[string key]
        => DataSets.TryGetValue(key, out var ds) ? ds : new();

    /// <summary>
    /// Para Transaction: indica si el SP reportó error (EsErrorDT != "0")
    /// </summary>
    public bool HasTransactionError { get; private set; }
    public string? TransactionMessage { get; private set; }

    // --- Factory methods ---

    public static DynResult Success(List<Dictionary<string, object?>> data)
        => new() { IsSuccess = true, Data = data };

    public static DynResult SuccessMulti(Dictionary<string, List<Dictionary<string, object?>>> dataSets)
        => new() { IsSuccess = true, DataSets = dataSets };

    public static DynResult SuccessTransaction(List<Dictionary<string, object?>> data, string message)
        => new() { IsSuccess = true, Data = data, TransactionMessage = message };

    public static DynResult Fail(string error)
        => new() { IsSuccess = false, Error = error };

    public static DynResult FailTransaction(string message)
        => new() { IsSuccess = false, HasTransactionError = true, TransactionMessage = message, Error = message };
}
