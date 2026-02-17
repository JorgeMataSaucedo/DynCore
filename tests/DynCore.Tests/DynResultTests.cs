namespace DynCore.Core.Tests;

public class DynResultTests
{
    [Fact]
    public void Success_SetsIsSuccessTrue()
    {
        var data = new List<Dictionary<string, object?>>
        {
            new() { ["Id"] = 1, ["Nombre"] = "Test" }
        };

        var result = DynResult.Success(data);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Single(result.Data);
        Assert.Equal(1, result.Data[0]["Id"]);
    }

    [Fact]
    public void Fail_SetsIsSuccessFalse_WithMessage()
    {
        var result = DynResult.Fail("SP no encontrado");

        Assert.False(result.IsSuccess);
        Assert.Equal("SP no encontrado", result.Error);
    }

    [Fact]
    public void FailTransaction_SetsTransactionError()
    {
        var result = DynResult.FailTransaction("Candidato ya existe");

        Assert.False(result.IsSuccess);
        Assert.True(result.HasTransactionError);
        Assert.Equal("Candidato ya existe", result.TransactionMessage);
        Assert.Equal("Candidato ya existe", result.Error);
    }

    [Fact]
    public void SuccessMulti_AllowsIndexerAccess()
    {
        var dataSets = new Dictionary<string, List<Dictionary<string, object?>>>
        {
            ["info"] = new() { new() { ["Id"] = 1 } },
            ["info2"] = new() { new() { ["Nombre"] = "Puestos" } }
        };

        var result = DynResult.SuccessMulti(dataSets);

        Assert.True(result.IsSuccess);
        Assert.Single(result["info"]);
        Assert.Single(result["info2"]);
        Assert.Equal(1, result["info"][0]["Id"]);
    }

    [Fact]
    public void Indexer_ReturnsEmptyList_WhenKeyNotFound()
    {
        var result = DynResult.Success(new());

        var data = result["noExiste"];

        Assert.NotNull(data);
        Assert.Empty(data);
    }

    [Fact]
    public void SuccessTransaction_SetsMessage()
    {
        var data = new List<Dictionary<string, object?>>
        {
            new() { ["Error"] = "0", ["Mensaje"] = "Guardado exitosamente" }
        };

        var result = DynResult.SuccessTransaction(data, "Guardado exitosamente");

        Assert.True(result.IsSuccess);
        Assert.Equal("Guardado exitosamente", result.TransactionMessage);
    }

    [Fact]
    public void TraceId_DefaultsToEmpty()
    {
        var result = DynResult.Success(new());

        Assert.Equal(Guid.Empty, result.TraceId);
    }

    [Fact]
    public void Result_NeverReturnsNull()
    {
        var success = DynResult.Success(new());
        var fail = DynResult.Fail("error");
        var failTx = DynResult.FailTransaction("error");
        var multi = DynResult.SuccessMulti(new());

        Assert.NotNull(success);
        Assert.NotNull(success.Data);
        Assert.NotNull(success.DataSets);
        Assert.NotNull(success.Lookups);

        Assert.NotNull(fail);
        Assert.NotNull(fail.Data);

        Assert.NotNull(failTx);
        Assert.NotNull(multi);
        Assert.NotNull(multi.DataSets);
    }
}
