using DynCore.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║     DynCore Demo - Prueba Real       ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();

// 1. Configuración
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .Build();

// 2. DI Container
var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(config);
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

// 3. Registrar DynCore - UNA LÍNEA
var commandsPath = Path.Combine(Directory.GetCurrentDirectory(), "Commands");
services.AddDynCore(opt =>
{
    opt.CommandsPath = commandsPath;
    opt.ErrorColumn = "Error";
    opt.MessageColumn = "Mensaje";
    opt.EnableHotReload = false;
});

var provider = services.BuildServiceProvider();
var engine = provider.GetRequiredService<IDynEngine>();

// ═══════════════════════════════════════
// TEST 1: Conexión básica
// ═══════════════════════════════════════
Console.WriteLine("── Test 1: Conexión a SQL Server ──");
var result1 = await engine.Execute("demo.version");

if (result1.IsSuccess)
{
    Console.WriteLine($"   OK - {result1.Data.Count} filas ({result1.ElapsedMs}ms)");
    if (result1.Data.Count > 0)
    {
        foreach (var kv in result1.Data[0].Take(3))
            Console.WriteLine($"   {kv.Key}: {kv.Value}");
    }
}
else
{
    Console.WriteLine($"   FAIL: {result1.Error}");
}

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 2: Query con datos
// ═══════════════════════════════════════
Console.WriteLine("── Test 2: Listar tablas ──");
var result2 = await engine.Execute("demo.tables");

if (result2.IsSuccess)
{
    Console.WriteLine($"   OK - {result2.Data.Count} tablas ({result2.ElapsedMs}ms)");
    foreach (var row in result2.Data.Take(5))
    {
        var name = row.GetValueOrDefault("TABLE_NAME") ?? "?";
        Console.WriteLine($"   → {name}");
    }
    if (result2.Data.Count > 5)
        Console.WriteLine($"   ... y {result2.Data.Count - 5} más");
}
else
{
    Console.WriteLine($"   FAIL: {result2.Error}");
}

Console.WriteLine();

// ═══════════════════════════════════════
// TEST 3: Comando que no existe
// ═══════════════════════════════════════
Console.WriteLine("── Test 3: Comando inexistente ──");
var result3 = await engine.Execute("no.existe");

if (!result3.IsSuccess)
    Console.WriteLine($"   OK (error esperado): {result3.Error}");
else
    Console.WriteLine("   FAIL: debería haber fallado!");

Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║            RESUMEN                   ║");
Console.WriteLine("╠══════════════════════════════════════╣");
Console.WriteLine($"║  Test 1 (conexión):  {(result1.IsSuccess ? "PASS" : "FAIL"),-16}║");
Console.WriteLine($"║  Test 2 (query):     {(result2.IsSuccess ? "PASS" : "FAIL"),-16}║");
Console.WriteLine($"║  Test 3 (error):     {(!result3.IsSuccess ? "PASS" : "FAIL"),-16}║");
Console.WriteLine("╚══════════════════════════════════════╝");
