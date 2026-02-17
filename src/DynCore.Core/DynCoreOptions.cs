namespace DynCore.Core;

/// <summary>
/// Opciones globales de DynCore. Configurable en AddDynCore().
/// Nada hardcodeado: si no te gustan los defaults, cámbialos.
/// </summary>
public class DynCoreOptions
{
    /// <summary>
    /// Ruta al directorio de comandos JSON.
    /// </summary>
    public string CommandsPath { get; set; } = "Commands";

    /// <summary>
    /// Columna que indica error en Transaction/MultiTransaction.
    /// Default: "EsErrorDT" (compatible con WCS).
    /// Cámbialo si tus SPs usan otra convención.
    /// </summary>
    public string ErrorColumn { get; set; } = "EsErrorDT";

    /// <summary>
    /// Columna con el mensaje de error/éxito en Transaction/MultiTransaction.
    /// Default: "MensajeDT" (compatible con WCS).
    /// </summary>
    public string MessageColumn { get; set; } = "MensajeDT";

    /// <summary>
    /// Valor de ErrorColumn que indica éxito (transaction commit).
    /// Default: "0" (compatible con WCS).
    /// </summary>
    public string SuccessValue { get; set; } = "0";

    /// <summary>
    /// Habilitar hot reload: recarga automática de JSONs cuando cambian en disco.
    /// Default: true.
    /// </summary>
    public bool EnableHotReload { get; set; } = true;

    /// <summary>
    /// Timeout global por defecto (segundos). Los comandos pueden override con su propio timeout.
    /// Default: 30.
    /// </summary>
    public int DefaultTimeout { get; set; } = 30;
}
