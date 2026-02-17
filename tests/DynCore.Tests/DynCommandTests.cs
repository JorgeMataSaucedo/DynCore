using System.Text.Json;

namespace DynCore.Core.Tests;

public class DynCommandTests
{
    [Fact]
    public void Deserialize_FullCommand()
    {
        var json = """
        {
            "id": "rh.candidates.list",
            "description": "Lista candidatos",
            "procedure": "precCandidateSel",
            "connection": "Operations",
            "strategy": "MultiResult",
            "timeout": 60,
            "cache": 300,
            "params": [
                { "name": "@pdFechaIni", "from": "FechaIni", "type": "date" },
                { "name": "@pnEstatus", "from": "EstatusId", "type": "int", "optional": true }
            ],
            "includes": ["rh.lookups.puestos", "rh.lookups.areas"]
        }
        """;

        var cmd = JsonSerializer.Deserialize<DynCommand>(json)!;

        Assert.Equal("rh.candidates.list", cmd.Id);
        Assert.Equal("precCandidateSel", cmd.Procedure);
        Assert.Equal("Operations", cmd.Connection);
        Assert.Equal("MultiResult", cmd.Strategy);
        Assert.Equal(60, cmd.Timeout);
        Assert.Equal(300, cmd.Cache);
        Assert.Equal(2, cmd.Params.Count);
        Assert.Equal("@pdFechaIni", cmd.Params[0].Name);
        Assert.Equal("FechaIni", cmd.Params[0].From);
        Assert.Equal("date", cmd.Params[0].Type);
        Assert.False(cmd.Params[0].Optional);
        Assert.True(cmd.Params[1].Optional);
        Assert.Equal(2, cmd.Includes.Count);
        Assert.Contains("rh.lookups.puestos", cmd.Includes);
    }

    [Fact]
    public void Deserialize_MinimalCommand_UsesDefaults()
    {
        var json = """
        {
            "id": "test",
            "procedure": "spTest",
            "connection": "Default"
        }
        """;

        var cmd = JsonSerializer.Deserialize<DynCommand>(json)!;

        Assert.Equal("Query", cmd.Strategy);
        Assert.Equal(30, cmd.Timeout);
        Assert.Equal(0, cmd.Cache);
        Assert.Empty(cmd.Params);
        Assert.Empty(cmd.Includes);
    }

    [Fact]
    public void DynContext_SupportsCustomTokens()
    {
        var context = new DynContext
        {
            UsuarioId = 42,
            UsuarioNombre = "TestUser"
        };
        context.Tokens["@@empresa@@"] = 1;
        context.Tokens["@@sucursal@@"] = "MTY";

        Assert.Equal(42, context.UsuarioId);
        Assert.Equal(1, context.Tokens["@@empresa@@"]);
        Assert.Equal("MTY", context.Tokens["@@sucursal@@"]);
    }

    [Fact]
    public void DynCoreOptions_HasSensibleDefaults()
    {
        var options = new DynCoreOptions();

        Assert.Equal("Commands", options.CommandsPath);
        Assert.Equal("EsErrorDT", options.ErrorColumn);
        Assert.Equal("MensajeDT", options.MessageColumn);
        Assert.Equal("0", options.SuccessValue);
        Assert.True(options.EnableHotReload);
        Assert.Equal(30, options.DefaultTimeout);
    }

    [Fact]
    public void DynCoreOptions_CanBeCustomized()
    {
        var options = new DynCoreOptions
        {
            ErrorColumn = "Error",
            MessageColumn = "Mensaje",
            SuccessValue = "OK",
            DefaultTimeout = 120
        };

        Assert.Equal("Error", options.ErrorColumn);
        Assert.Equal("Mensaje", options.MessageColumn);
        Assert.Equal("OK", options.SuccessValue);
        Assert.Equal(120, options.DefaultTimeout);
    }
}
