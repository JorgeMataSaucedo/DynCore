using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DynCore.Core;

/// <summary>
/// Registro de comandos DynCore. Carga definiciones desde archivos JSON.
/// Soporta hot reload: si cambias un JSON en disco, se recarga automáticamente.
/// </summary>
public class DynRegistry : IDisposable
{
    private readonly Dictionary<string, DynCommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DynRegistry> _logger;
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;

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

    /// <summary>
    /// Obtiene un comando por su ID.
    /// </summary>
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

    /// <summary>
    /// Intenta obtener un comando. Regresa false si no existe.
    /// </summary>
    public bool TryGet(string commandId, out DynCommand? command)
    {
        lock (_lock)
            return _commands.TryGetValue(commandId, out command);
    }

    /// <summary>
    /// Lista todos los IDs de comandos registrados.
    /// </summary>
    public IReadOnlyCollection<string> GetAllIds()
    {
        lock (_lock)
            return _commands.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Lista todos los comandos registrados.
    /// </summary>
    public IReadOnlyCollection<DynCommand> GetAll()
    {
        lock (_lock)
            return _commands.Values.ToList().AsReadOnly();
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

            _commands[cmd.Id] = cmd;
            _logger.LogDebug("DynRegistry: '{Id}' cargado desde {File}", cmd.Id, file);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "DynRegistry: Error parseando '{File}'", file);
        }
    }

    // =====================================================================
    // HOT RELOAD
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
        // Delay breve para evitar lecturas parciales (el SO avisa antes de terminar de escribir)
        Task.Delay(100).ContinueWith(_ =>
        {
            try
            {
                lock (_lock)
                    LoadSingleFile(e.FullPath);

                _logger.LogInformation("DynRegistry: Recargado '{File}'", e.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DynRegistry: Error recargando '{File}'", e.Name);
            }
        });
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            var toRemove = _commands.Where(kv => true).FirstOrDefault(kv =>
            {
                // Buscar qué comando venía de este archivo por ID derivado del nombre
                var fileName = Path.GetFileNameWithoutExtension(e.Name);
                return kv.Key.Equals(fileName, StringComparison.OrdinalIgnoreCase);
            });

            if (toRemove.Key != null)
            {
                _commands.Remove(toRemove.Key);
                _logger.LogInformation("DynRegistry: Comando '{Id}' removido (archivo eliminado)", toRemove.Key);
            }
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        OnFileDeleted(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted, Path.GetDirectoryName(e.OldFullPath)!, e.OldName!));
        OnFileChanged(sender, e);
    }

    // =====================================================================
    // DISPOSE
    // =====================================================================

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
    }
}
