namespace DynCore.Core;

/// <summary>
/// Interfaz del motor DynCore. Para DI y testing.
/// </summary>
public interface IDynEngine
{
    Task<DynResult> Execute(string commandId, object? parameters = null);
}
