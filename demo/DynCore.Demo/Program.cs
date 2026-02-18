using DynCore.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║   DynCore Demo - Prueba de 4 Estrategias     ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine();

// 1. Configuración
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

// 2. DI Container
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

// 3. Registrar DynCore
var commandsPath = Path.Combine(Directory.GetCurrentDirectory(), "Commands");
services.AddDynCore(opt =>
{
    opt.CommandsPath = commandsPath;
    opt.ErrorColumn = "Error";
    opt.MessageColumn = "Mensaje";
    opt.EnableHotReload = false;
});

var provider = services.BuildServiceProvider();

// Simular usuario autenticado (como haría Blazor)
var context = provider.GetRequiredService<DynContext>();
context.UsuarioId = 42;
context.UsuarioNombre = "Jorge Test";

var engine = provider.GetRequiredService<IDynEngine>();

var results = new Dictionary<string, bool>();

// ═══════════════════════════════════════
// TEST 1: QUERY - Lectura simple
// ═══════════════════════════════════════
Console.WriteLine("── Test 1: Query (lectura simple) ──");
var r1 = await engine.Execute("test.query", new { top = 5 });
results["Query"] = r1.IsSuccess;

if (r1.IsSuccess)
{
    Console.WriteLine($"   OK - {r1.Data.Count} comandos ({r1.ElapsedMs}ms) [trace:{r1.TraceId.ToString("N")[..8]}]");
    foreach (var row in r1.Data)
    {
        var id = row.GetValueOrDefault("ComandoSQLId");
        var desc = row.GetValueOrDefault("ComandoSQLDesc") ?? "?";
        var sp = row.GetValueOrDefault("ComandoCmd") ?? "?";
        Console.WriteLine($"   #{id} {desc} -> {sp}");
    }
}
else
    Console.WriteLine($"   FAIL: {r1.Error}");

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 2: TRANSACTION - Commit exitoso
// ═══════════════════════════════════════
Console.WriteLine("── Test 2: Transaction (commit) ──");
var r2 = await engine.Execute("test.transaction", new { nombre = "DynCore Test" });
results["Tx Commit"] = r2.IsSuccess;

if (r2.IsSuccess)
{
    Console.WriteLine($"   OK - Commit! ({r2.ElapsedMs}ms)");
    Console.WriteLine($"   Mensaje: {r2.TransactionMessage}");
}
else
    Console.WriteLine($"   FAIL: {r2.Error}");

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 3: TRANSACTION - Rollback esperado
// ═══════════════════════════════════════
Console.WriteLine("── Test 3: Transaction (rollback esperado) ──");
var r3 = await engine.Execute("test.transaction", new { nombre = "" });
results["Tx Rollback"] = r3.HasTransactionError;

if (r3.HasTransactionError)
{
    Console.WriteLine($"   OK - Rollback correcto! ({r3.ElapsedMs}ms)");
    Console.WriteLine($"   Mensaje: {r3.TransactionMessage}");
}
else
    Console.WriteLine($"   FAIL: debería haber hecho rollback");

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 4: MULTIRESULT - 3 datasets
// ═══════════════════════════════════════
Console.WriteLine("── Test 4: MultiResult (3 datasets) ──");
var r4 = await engine.Execute("test.multi");
results["MultiResult"] = r4.IsSuccess && r4.DataSets.Count >= 3;

if (r4.IsSuccess)
{
    Console.WriteLine($"   OK - {r4.DataSets.Count} datasets ({r4.ElapsedMs}ms)");
    foreach (var ds in r4.DataSets)
    {
        Console.WriteLine($"   [{ds.Key}] {ds.Value.Count} filas");
        if (ds.Value.Count > 0)
        {
            var first = ds.Value[0];
            var preview = string.Join(", ", first.Take(3).Select(kv => $"{kv.Key}={kv.Value}"));
            Console.WriteLine($"      -> {preview}");
        }
    }
}
else
    Console.WriteLine($"   FAIL: {r4.Error}");

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 5: QUERY + INCLUDES (combos)
// ═══════════════════════════════════════
Console.WriteLine("── Test 5: Query + Includes (combos) ──");
var r5 = await engine.Execute("test.query.with-includes", new { top = 3 });
results["Includes"] = r5.IsSuccess && r5.Lookups.Count > 0;

if (r5.IsSuccess)
{
    Console.WriteLine($"   OK - {r5.Data.Count} filas + {r5.Lookups.Count} lookups ({r5.ElapsedMs}ms)");
    foreach (var lookup in r5.Lookups)
    {
        Console.WriteLine($"   Lookup [{lookup.Key}]: {lookup.Value.Count} items");
        foreach (var item in lookup.Value)
        {
            var nombre = item.GetValueOrDefault("Nombre") ?? "?";
            Console.WriteLine($"      -> {nombre}");
        }
    }
}
else
    Console.WriteLine($"   FAIL: {r5.Error}");

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 6: CACHE - Segundo call debe ser instantáneo
// ═══════════════════════════════════════
Console.WriteLine("── Test 6: Cache (segundo call instantáneo) ──");
var r6a = await engine.Execute("test.lookup");
var r6b = await engine.Execute("test.lookup");
results["Cache"] = r6a.IsSuccess && r6b.IsSuccess && r6b.ElapsedMs <= r6a.ElapsedMs;

Console.WriteLine($"   1er call: {r6a.ElapsedMs}ms");
Console.WriteLine($"   2do call: {r6b.ElapsedMs}ms (cache hit)");
Console.WriteLine($"   {(r6b.ElapsedMs <= r6a.ElapsedMs ? "OK - Cache funcionando" : "WARN - Revisar cache")}");

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 7: ERROR - Comando que no existe
// ═══════════════════════════════════════
Console.WriteLine("── Test 7: Comando inexistente ──");
var r7 = await engine.Execute("no.existe");
results["Error"] = !r7.IsSuccess;

Console.WriteLine($"   {(!r7.IsSuccess ? "OK" : "FAIL")} - {r7.Error}");

Console.WriteLine();

// ═══════════════════════════════════════
// RESUMEN
// ═══════════════════════════════════════
Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║               RESUMEN FINAL                  ║");
Console.WriteLine("╠══════════════════════════════════════════════╣");
foreach (var r in results)
    Console.WriteLine($"║  {r.Key,-16} {(r.Value ? "PASS" : "FAIL"),-27}║");
Console.WriteLine("╠══════════════════════════════════════════════╣");
var total = results.Count(r => r.Value);
Console.WriteLine($"║  {total}/{results.Count} tests passed{"",-28}║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
