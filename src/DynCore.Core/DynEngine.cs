using System.Data;
using System.Diagnostics;
using System.Text.Json;
using System.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DynCore.Core;

/// <summary>
/// Motor de ejecución DynCore.
/// Un solo método Execute() que reemplaza WCSCoreBL + WCSCoreDA.
///
/// Estrategias:
///   "Query"            → Lectura simple, sin transacción
///   "Transaction"      → Escritura con TransactionScope
///   "MultiResult"      → Múltiples datasets, sin transacción
///   "MultiTransaction" → Múltiples datasets, con TransactionScope
///
/// Combos se manejan con "includes" en cualquier estrategia.
/// </summary>
public class DynEngine : IDynEngine
{
    private readonly DynRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DynEngine> _logger;
    private readonly DynContext? _context;
    private readonly DynCoreOptions _options;
    private readonly IMemoryCache? _cache;

    public DynEngine(
        DynRegistry registry,
        IConfiguration configuration,
        ILogger<DynEngine> logger,
        DynCoreOptions options,
        DynContext? context = null,
        IMemoryCache? cache = null)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
        _options = options;
        _context = context;
        _cache = cache;
    }

    /// <summary>
    /// Ejecuta un comando DynCore.
    /// </summary>
    public async Task<DynResult> Execute(string commandId, object? parameters = null)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var cmd = _registry.Get(commandId);

            // Verificar caché
            if (cmd.Cache > 0 && _cache != null)
            {
                var cacheKey = BuildCacheKey(commandId, parameters);
                if (_cache.TryGetValue(cacheKey, out DynResult? cached) && cached != null)
                {
                    _logger.LogDebug("DynCore: {CommandId} → CACHE HIT", commandId);
                    return cached;
                }
            }

            var connStr = GetConnectionString(cmd.Connection);
            var sqlParams = MapParameters(cmd, parameters);

            _logger.LogInformation("DynCore: [{Strategy}] {CommandId} → {SP} (timeout:{Timeout}s)",
                cmd.Strategy, commandId, cmd.Procedure, cmd.Timeout);

            var result = cmd.Strategy.ToLowerInvariant() switch
            {
                "query"            => await ExecuteQuery(connStr, cmd, sqlParams),
                "transaction"      => await ExecuteTransaction(connStr, cmd, sqlParams),
                "multiresult"      => await ExecuteMultiResult(connStr, cmd, sqlParams),
                "multitransaction" => await ExecuteMultiTransaction(connStr, cmd, sqlParams),
                _ => DynResult.Fail(
                    $"Estrategia '{cmd.Strategy}' no reconocida. " +
                    "Opciones: Query, Transaction, MultiResult, MultiTransaction")
            };

            // Ejecutar includes (combos/lookups)
            if (result.IsSuccess && cmd.Includes.Count > 0)
                await ExecuteIncludes(cmd, parameters, result);

            sw.Stop();
            result.CommandId = commandId;
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.TraceId = Guid.NewGuid();

            // Guardar en caché si aplica, vinculado al token de invalidación del registry
            if (result.IsSuccess && cmd.Cache > 0 && _cache != null)
            {
                var cacheKey = BuildCacheKey(commandId, parameters);
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(cmd.Cache))
                    .AddExpirationToken(_registry.GetCacheToken(commandId));
                _cache.Set(cacheKey, result, cacheOptions);
                _logger.LogDebug("DynCore: {CommandId} → CACHED ({Seconds}s)", commandId, cmd.Cache);
            }

            _logger.LogInformation("DynCore: [{TraceId}] {CommandId} → {Status} ({Ms}ms){Includes}",
                result.TraceId.ToString("N")[..8],
                commandId,
                result.IsSuccess ? "OK" : "FAIL",
                result.ElapsedMs,
                cmd.Includes.Count > 0 ? $" +{cmd.Includes.Count} lookups" : "");

            return result;
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogError(ex, "DynCore: Comando no encontrado '{CommandId}'", commandId);
            return DynResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DynCore: Error ejecutando '{CommandId}'", commandId);
            return DynResult.Fail($"Error en '{commandId}': {ex.Message}");
        }
    }

    // =====================================================================
    // ESTRATEGIAS
    // =====================================================================

    private async Task<DynResult> ExecuteQuery(string connStr, DynCommand cmd, SqlParameter[] sqlParams)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var sqlCmd = CreateSqlCommand(cmd, conn, sqlParams);

        var data = await ReadSingleResultAsync(sqlCmd);
        return DynResult.Success(data);
    }

    private async Task<DynResult> ExecuteTransaction(string connStr, DynCommand cmd, SqlParameter[] sqlParams)
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var sqlCmd = CreateSqlCommand(cmd, conn, sqlParams);

        var data = await ReadSingleResultAsync(sqlCmd);
        return CompleteTransaction(scope, cmd, data);
    }

    private async Task<DynResult> ExecuteMultiResult(string connStr, DynCommand cmd, SqlParameter[] sqlParams)
    {
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var sqlCmd = CreateSqlCommand(cmd, conn, sqlParams);

        var dataSets = await ReadMultiResultAsync(sqlCmd);
        return DynResult.SuccessMulti(dataSets);
    }

    private async Task<DynResult> ExecuteMultiTransaction(string connStr, DynCommand cmd, SqlParameter[] sqlParams)
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var sqlCmd = CreateSqlCommand(cmd, conn, sqlParams);

        var dataSets = await ReadMultiResultAsync(sqlCmd);

        if (dataSets.TryGetValue("info", out var infoRows) && infoRows.Count > 0)
        {
            var firstRow = infoRows[0];
            var errorCol = _options.ErrorColumn;
            var msgCol = _options.MessageColumn;

            if (!firstRow.ContainsKey(errorCol))
            {
                _logger.LogWarning("DynCore: SP '{SP}' (MultiTransaction) no regresó '{Col}'. Rollback.",
                    cmd.Procedure, errorCol);
                return DynResult.Fail(
                    $"SP '{cmd.Procedure}' no regresó '{errorCol}'. " +
                    $"MultiTransaction requiere SELECT @nError AS {errorCol}, @sMensaje AS {msgCol}");
            }

            var esError = firstRow[errorCol]?.ToString() ?? "1";
            var mensaje = firstRow.GetValueOrDefault(msgCol)?.ToString() ?? "";

            if (esError != _options.SuccessValue)
            {
                _logger.LogWarning("DynCore: MultiTransaction rollback '{SP}': {Msg}", cmd.Procedure, mensaje);
                return DynResult.FailTransaction(
                    string.IsNullOrEmpty(mensaje) ? $"SP '{cmd.Procedure}' reportó error ({errorCol}={esError})" : mensaje);
            }

            scope.Complete();
            return DynResult.SuccessMulti(dataSets);
        }

        _logger.LogWarning("DynCore: SP '{SP}' (MultiTransaction) sin datos. Rollback.", cmd.Procedure);
        return DynResult.Fail($"SP '{cmd.Procedure}' no regresó resultados en dataset principal");
    }

    // =====================================================================
    // INCLUDES (COMBOS)
    // =====================================================================

    private async Task ExecuteIncludes(DynCommand mainCmd, object? parameters, DynResult result)
    {
        var tasks = mainCmd.Includes.Select(id => ExecuteSingleInclude(id, parameters));
        var results = await Task.WhenAll(tasks);

        foreach (var (id, data) in results)
            result.Lookups[id] = data;
    }

    private async Task<(string id, List<Dictionary<string, object?>> data)> ExecuteSingleInclude(
        string includeId, object? parameters)
    {
        try
        {
            // Los includes también pueden usar caché
            if (_cache != null)
            {
                if (_registry.TryGet(includeId, out var cachedCmd) && cachedCmd != null && cachedCmd.Cache > 0)
                {
                    var cacheKey = BuildCacheKey(includeId, parameters);
                    if (_cache.TryGetValue(cacheKey, out List<Dictionary<string, object?>>? cachedData) && cachedData != null)
                    {
                        _logger.LogDebug("DynCore: Include '{Id}' → CACHE HIT", includeId);
                        return (includeId, cachedData);
                    }
                }
            }

            if (!_registry.TryGet(includeId, out var cmd) || cmd == null)
            {
                _logger.LogWarning("DynCore: Include '{Id}' no encontrado, se omite", includeId);
                return (includeId, new());
            }

            var connStr = GetConnectionString(cmd.Connection);
            var sqlParams = MapParameters(cmd, parameters);

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();
            await using var sqlCmd = CreateSqlCommand(cmd, conn, sqlParams);

            var data = await ReadSingleResultAsync(sqlCmd);

            // Cachear el include si tiene caché configurado, con token de invalidación
            if (cmd.Cache > 0 && _cache != null)
            {
                var cacheKey = BuildCacheKey(includeId, parameters);
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(cmd.Cache))
                    .AddExpirationToken(_registry.GetCacheToken(includeId));
                _cache.Set(cacheKey, data, cacheOptions);
            }

            _logger.LogDebug("DynCore: Include '{Id}' → {Rows} filas", includeId, data.Count);
            return (includeId, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DynCore: Error en include '{Id}', se omite", includeId);
            return (includeId, new());
        }
    }

    // =====================================================================
    // TRANSACTION HELPER
    // =====================================================================

    private DynResult CompleteTransaction(TransactionScope scope, DynCommand cmd, List<Dictionary<string, object?>> data)
    {
        if (data.Count == 0)
        {
            _logger.LogWarning("DynCore: SP '{SP}' no regresó filas. Rollback.", cmd.Procedure);
            return DynResult.Fail($"SP '{cmd.Procedure}' no regresó resultados");
        }

        var firstRow = data[0];
        var errorCol = _options.ErrorColumn;
        var msgCol = _options.MessageColumn;

        if (!firstRow.ContainsKey(errorCol))
        {
            _logger.LogWarning("DynCore: SP '{SP}' no regresó '{Col}'. Rollback.", cmd.Procedure, errorCol);
            return DynResult.Fail(
                $"SP '{cmd.Procedure}' no regresó '{errorCol}'. " +
                $"Transacciones requieren SELECT @nError AS {errorCol}, @sMensaje AS {msgCol}");
        }

        var esError = firstRow[errorCol]?.ToString() ?? "1";
        var mensaje = firstRow.GetValueOrDefault(msgCol)?.ToString() ?? "";

        if (esError != _options.SuccessValue)
        {
            _logger.LogWarning("DynCore: Transaction rollback '{SP}': {Msg}", cmd.Procedure, mensaje);
            return DynResult.FailTransaction(
                string.IsNullOrEmpty(mensaje) ? $"SP '{cmd.Procedure}' reportó error ({errorCol}={esError})" : mensaje);
        }

        scope.Complete();
        return DynResult.SuccessTransaction(data, mensaje);
    }

    // =====================================================================
    // SQL HELPERS
    // =====================================================================

    private SqlCommand CreateSqlCommand(DynCommand cmd, SqlConnection conn, SqlParameter[] sqlParams)
    {
        var sqlCmd = new SqlCommand(cmd.Procedure, conn)
        {
            CommandType = CommandType.StoredProcedure,
            CommandTimeout = cmd.Timeout > 0 ? cmd.Timeout : _options.DefaultTimeout
        };
        sqlCmd.Parameters.AddRange(sqlParams);
        return sqlCmd;
    }

    private async Task<List<Dictionary<string, object?>>> ReadSingleResultAsync(SqlCommand cmd)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    private async Task<Dictionary<string, List<Dictionary<string, object?>>>> ReadMultiResultAsync(SqlCommand cmd)
    {
        var dataSets = new Dictionary<string, List<Dictionary<string, object?>>>();
        await using var reader = await cmd.ExecuteReaderAsync();

        int idx = 0;
        do
        {
            var key = idx == 0 ? "info" : $"info{idx + 1}";
            var rows = new List<Dictionary<string, object?>>();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }

            dataSets[key] = rows;
            idx++;
        } while (await reader.NextResultAsync());

        return dataSets;
    }

    // =====================================================================
    // PARAMETER MAPPING
    // =====================================================================

    private SqlParameter[] MapParameters(DynCommand cmd, object? parameters)
    {
        if (cmd.Params.Count == 0)
            return Array.Empty<SqlParameter>();

        var dict = parameters != null ? ObjectToDictionary(parameters) : new();
        var sqlParams = new List<SqlParameter>();

        foreach (var p in cmd.Params)
        {
            object? value = ResolveParamValue(p, dict);

            if (value == null && !p.Optional)
                throw new ArgumentException(
                    $"Parámetro requerido '{p.From}' no encontrado para '{cmd.Id}'. " +
                    $"Recibidos: [{string.Join(", ", dict.Keys)}]");

            sqlParams.Add(new SqlParameter(p.Name, ConvertToSqlType(p.Type))
            {
                Value = value ?? DBNull.Value
            });
        }

        return sqlParams.ToArray();
    }

    private object? ResolveParamValue(DynParam param, Dictionary<string, object?> dict)
    {
        var from = param.From;

        if (from.Equals("@@usuario@@", StringComparison.OrdinalIgnoreCase))
        {
            if (_context == null)
                throw new InvalidOperationException("'@@usuario@@' requiere DynContext registrado en DI.");
            if (_context.UsuarioId == 0)
                _logger.LogWarning("DynCore: @@usuario@@ es 0. Asegure configurar DynContext.UsuarioId en su sesión.");
            return _context.UsuarioId;
        }

        // @@token@@ custom → buscar en DynContext.Tokens, error claro si no existe
        if (from.StartsWith("@@") && from.EndsWith("@@"))
        {
            if (_context?.Tokens == null)
                throw new InvalidOperationException($"Token '{from}' requiere DynContext registrado en DI.");
            if (_context.Tokens.TryGetValue(from, out var tokenValue))
                return tokenValue;
            throw new InvalidOperationException(
                $"Token '{from}' no encontrado en DynContext.Tokens. Tokens registrados: [{string.Join(", ", _context.Tokens.Keys)}]");
        }

        if (dict.TryGetValue(from, out var value))
            return value;

        return null;
    }

    private Dictionary<string, object?> ObjectToDictionary(object obj)
    {
        if (obj is Dictionary<string, object?> dict)
            return new Dictionary<string, object?>(dict, StringComparer.OrdinalIgnoreCase);
        if (obj is Dictionary<string, object> d2)
            return new Dictionary<string, object?>(
                d2.Select(kv => KeyValuePair.Create(kv.Key, (object?)kv.Value)),
                StringComparer.OrdinalIgnoreCase);
        if (obj is JsonElement je) return JsonElementToDict(je);

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.GetType().GetProperties())
            result[prop.Name] = prop.GetValue(obj);
        return result;
    }

    private Dictionary<string, object?> JsonElementToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetDecimal(),
                    JsonValueKind.True   => true,
                    JsonValueKind.False  => false,
                    JsonValueKind.Null   => null,
                    _                    => prop.Value.ToString()
                };
            }
        }
        return dict;
    }

    private SqlDbType ConvertToSqlType(string type) => type.ToLowerInvariant() switch
    {
        "int"      => SqlDbType.Int,
        "bigint"   => SqlDbType.BigInt,
        "string"   => SqlDbType.NVarChar,
        "date"     => SqlDbType.DateTime,
        "datetime" => SqlDbType.DateTime,
        "bit"      => SqlDbType.Bit,
        "decimal"  => SqlDbType.Decimal,
        "float"    => SqlDbType.Float,
        "json"     => SqlDbType.NVarChar,
        "guid"     => SqlDbType.UniqueIdentifier,
        _          => SqlDbType.NVarChar
    };

    private string GetConnectionString(string name)
    {
        var connStr = _configuration.GetConnectionString(name);
        if (string.IsNullOrEmpty(connStr))
            throw new InvalidOperationException(
                $"Connection string '{name}' no encontrada. Agregue ConnectionStrings:{name} en appsettings.json");
        return connStr;
    }

    // =====================================================================
    // CACHE
    // =====================================================================

    private string BuildCacheKey(string commandId, object? parameters)
    {
        if (parameters == null) return $"dyncore:{commandId}";
        var dict = ObjectToDictionary(parameters);
        var sorted = dict.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
        var paramJson = JsonSerializer.Serialize(sorted);
        return $"dyncore:{commandId}:{paramJson}";
    }
}
