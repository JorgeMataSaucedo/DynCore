namespace DynCore.Core;

/// <summary>
/// Contexto de sesión para DynCore.
/// Reemplaza el token @@usuario@@ de WCS con algo tipado y extensible.
/// Se inyecta por DI como Scoped (uno por conexión/sesión de usuario).
/// </summary>
public class DynContext
{
    /// <summary>
    /// ID del usuario actual. Reemplaza @@usuario@@ de WCS.
    /// </summary>
    public int UsuarioId { get; set; }

    /// <summary>
    /// Nombre del usuario actual (para logging/audit).
    /// </summary>
    public string UsuarioNombre { get; set; } = string.Empty;

    /// <summary>
    /// Valores custom que se pueden inyectar en cualquier parámetro.
    /// Ejemplo: context.Tokens["@@empresa@@"] = 1;
    /// En el JSON del param: "from": "@@empresa@@"
    /// </summary>
    public Dictionary<string, object?> Tokens { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
