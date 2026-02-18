using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Logging;

namespace DynCore.Core;

/// <summary>
/// Registro de comandos DynCore. Carga definiciones desde archivos JSON.
/// Soporta hot reload con debounce y retry.
/// Expone CancellationChangeTokens para invalidar cache al recargar comandos.
/// </summary>
public class DynRegistry : IDisposable
{
    private readonly Dictionary<string, DynCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fileToCommandId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceTokens = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cacheTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DynRegistry> _logger;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;
    private const int DebounceMs = 500;
    private const int MaxRetries = 3;

    public DynRegistry(ILogger<DynRegistry> logger)
    {
        _logger = logger;
    }

    public int Count
    {
        get { lock (_lock) return _commands.Count; }
    }

    /// <summary>
    /// Carga todos los archivos .json de un directorio (recursivo).
    /// </summary>
    public void LoadFromDirectory(string path, bool enableHotReload = true)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogWarning("DynRegistry: Directorio '{Path}' no existe", path);
            return;
        }

        var fullPath = Path.GetFullPath(path);
        LoadAllFiles(fullPath);

        if (enableHotReload)
            StartWatching(fullPath);
    }

    public DynCommand Get(string commandId)
    {
        lock (_lock)
        {
            if (_commands.TryGetValue(commandId, out var cmd))
                return cmd;
        }

        throw new KeyNotFoundException(
            $"Comando '{commandId}' no encontrado. Disponibles: {string.Join(", ", GetAllIds().Take(10))}");
    }

    public bool TryGet(string commandId, out DynCommand? command)
    {
        lock (_lock)
            return _commands.TryGetValue(commandId, out command);
    }

    public IReadOnlyCollection<string> GetAllIds()
    {
        lock (_lock)
            return _commands.Keys.ToList().AsReadOnly();
    }

    public IReadOnlyCollection<DynCommand> GetAll()
    {
        lock (_lock)
            return _commands.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Obtiene un IChangeToken vinculado a un commandId.
    /// Se cancela automáticamente cuando el comando se recarga o elimina por hot reload,
    /// lo que causa que IMemoryCache evicte las entradas vinculadas.
    /// </summary>
    public IChangeToken GetCacheToken(string commandId)
    {
        var cts = _cacheTokens.GetOrAdd(commandId, _ => new CancellationTokenSource());
        return new CancellationChangeToken(cts.Token);
    }

    private void InvalidateCacheToken(string commandId)
    {
        if (_cacheTokens.TryRemove(commandId, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
            _logger.LogDebug("DynRegistry: Cache invalidado para '{Id}'", commandId);
        }
    }

    // =====================================================================
    // FILE LOADING
    // =====================================================================

    private void LoadAllFiles(string path)
    {
        var files = Directory.GetFiles(path, "*.json", SearchOption.AllDirectories);

        lock (_lock)
        {
            _commands.Clear();
            _fileToCommandId.Clear();
            foreach (var file in files)
                LoadSingleFile(file);
        }

        _logger.LogInformation("DynRegistry: {Count} comandos cargados desde '{Path}'", _commands.Count, path);
    }

    private void LoadSingleFile(string file)
    {
        try
        {
            var json = File.ReadAllText(file);
            var cmd = JsonSerializer.Deserialize<DynCommand>(json);

            if (cmd == null || string.IsNullOrWhiteSpace(cmd.Id))
            {
                _logger.LogWarning("DynRegistry: '{File}' formato inválido, se omite", file);
                return;
            }

            var normalizedPath = Path.GetFullPath(file);

            // Si este archivo ya tenía un comando con otro ID, remover el viejo
            if (_fileToCommandId.TryGetValue(normalizedPath, out var oldId) && oldId != cmd.Id)
            {
                _commands.Remove(oldId);
                InvalidateCacheToken(oldId);
            }

            // Si el comando ya existía (recarga), invalidar su cache
            if (_commands.ContainsKey(cmd.Id))
                InvalidateCacheToken(cmd.Id);

            _commands[cmd.Id] = cmd;
            _fileToCommandId[normalizedPath] = cmd.Id;
            _logger.LogDebug("DynRegistry: '{Id}' cargado desde {File}", cmd.Id, file);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "DynRegistry: Error parseando '{File}'", file);
        }
    }

    // =====================================================================
    // HOT RELOAD - con debounce y retry
    // =====================================================================

    private void StartWatching(string path)
    {
        _watcher = new FileSystemWatcher(path, "*.json")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;
        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("DynRegistry: Hot reload activado en '{Path}'", path);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        DebounceReload(e.FullPath);
    }

    /// <summary>
    /// Debounce: si llegan múltiples eventos para el mismo archivo en 500ms,
    /// solo procesa el último. Evita recargas innecesarias.
    /// </summary>
    private void DebounceReload(string filePath)
    {
        // Cancelar debounce anterior para este archivo
        if (_debounceTokens.TryRemove(filePath, out var oldCts))
            oldCts.Cancel();

        var cts = new CancellationTokenSource();
        _debounceTokens[filePath] = cts;

        Task.Delay(DebounceMs, cts.Token).ContinueWith(_ =>
        {
            _debounceTokens.TryRemove(filePath, out CancellationTokenSource? _);
            ReloadFileWithRetry(filePath);
        }, cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    /// <summary>
    /// Retry: intenta leer el archivo hasta 3 veces con delay creciente.
    /// Evita fallos por archivos aún bloqueados por el editor.
    /// </summary>
    private void ReloadFileWithRetry(string filePath)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                lock (_lock)
                    LoadSingleFile(filePath);

                _logger.LogInformation("DynRegistry: Recargado '{File}'", Path.GetFileName(filePath));
                return;
            }
            catch (IOException) when (attempt < MaxRetries)
            {
                Thread.Sleep(200 * attempt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DynRegistry: Error recargando '{File}' (intento {Attempt}/{Max})",
                    Path.GetFileName(filePath), attempt, MaxRetries);
            }
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        var normalizedPath = Path.GetFullPath(e.FullPath);

        lock (_lock)
        {
            if (_fileToCommandId.TryGetValue(normalizedPath, out var commandId))
            {
                _commands.Remove(commandId);
                _fileToCommandId.Remove(normalizedPath);
                InvalidateCacheToken(commandId);
                _logger.LogInformation("DynRegistry: Comando '{Id}' removido (archivo eliminado)", commandId);
            }
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Remover el viejo
        var oldPath = Path.GetFullPath(e.OldFullPath);
        lock (_lock)
        {
            if (_fileToCommandId.TryGetValue(oldPath, out var oldId))
            {
                _commands.Remove(oldId);
                _fileToCommandId.Remove(oldPath);
                InvalidateCacheToken(oldId);
            }
        }

        // Cargar el nuevo
        DebounceReload(e.FullPath);
    }

    // =====================================================================
    // DISPOSE
    // =====================================================================

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        foreach (var cts in _debounceTokens.Values)
            cts.Cancel();
        _debounceTokens.Clear();

        foreach (var cts in _cacheTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _cacheTokens.Clear();
    }
}
