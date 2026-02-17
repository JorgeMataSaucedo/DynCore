using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DynCore.Core.Tests;

public class DynRegistryTests : IDisposable
{
    private readonly DynRegistry _registry;
    private readonly string _tempDir;

    public DynRegistryTests()
    {
        _registry = new DynRegistry(NullLogger<DynRegistry>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"dyncore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void LoadFromDirectory_LoadsJsonFiles()
    {
        WriteCommand("test.query", "TestSP", "Query");
        WriteCommand("test.multi", "TestSP2", "MultiResult");

        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        Assert.Equal(2, _registry.Count);
    }

    [Fact]
    public void Get_ReturnsCommand_WhenExists()
    {
        WriteCommand("rh.candidates.list", "precCandidateSel", "MultiResult");
        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        var cmd = _registry.Get("rh.candidates.list");

        Assert.Equal("precCandidateSel", cmd.Procedure);
        Assert.Equal("MultiResult", cmd.Strategy);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        WriteCommand("rh.candidates.list", "precCandidateSel", "Query");
        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        var cmd = _registry.Get("RH.CANDIDATES.LIST");

        Assert.Equal("precCandidateSel", cmd.Procedure);
    }

    [Fact]
    public void Get_ThrowsClearError_WhenNotFound()
    {
        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        var ex = Assert.Throws<KeyNotFoundException>(() => _registry.Get("no.existe"));

        Assert.Contains("no.existe", ex.Message);
        Assert.Contains("no encontrado", ex.Message);
    }

    [Fact]
    public void TryGet_ReturnsFalse_WhenNotFound()
    {
        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        var found = _registry.TryGet("no.existe", out var cmd);

        Assert.False(found);
        Assert.Null(cmd);
    }

    [Fact]
    public void LoadFromDirectory_SkipsInvalidJson()
    {
        WriteCommand("valid.command", "SP1", "Query");
        File.WriteAllText(Path.Combine(_tempDir, "broken.json"), "{ INVALID JSON }}}");

        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void LoadFromDirectory_SkipsEmptyId()
    {
        File.WriteAllText(Path.Combine(_tempDir, "empty.json"),
            """{"id": "", "procedure": "SP1", "strategy": "Query"}""");

        _registry.LoadFromDirectory(_tempDir, enableHotReload: false);

        Assert.Equal(0, _registry.Count);
    }

    [Fact]
    public void LoadFromDirectory_NonExistentPath_DoesNotThrow()
    {
        _registry.LoadFromDirectory("/ruta/que/no/existe", enableHotReload: false);

        Assert.Equal(0, _registry.Count);
    }

    private void WriteCommand(string id, string procedure, string strategy)
    {
        var json = $$"""
        {
            "id": "{{id}}",
            "procedure": "{{procedure}}",
            "connection": "Test",
            "strategy": "{{strategy}}"
        }
        """;
        File.WriteAllText(Path.Combine(_tempDir, $"{id}.json"), json);
    }

    public void Dispose()
    {
        _registry.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
