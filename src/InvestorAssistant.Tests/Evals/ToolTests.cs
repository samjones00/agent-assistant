using System.Text.Json;
using InvestorAssistant.Tools;

namespace InvestorAssistant.Tests.Evals;

public sealed class ToolTests
{
    public ToolTests()
    {
        var dataDir = EvalHelpers.GetDataDir();
        QueryCsvTool.Configure(dataDir);
        ColumnMappings.Load(dataDir);
        CalcTool.LoadFxRates(dataDir);
    }

    [Fact]
    public async Task QueryCsv_Investors_ReturnsInvestor()
    {
        var result = await QueryCsvTool.QueryCsvAsync("investors", """{"investor_id":"INV001"}""", "investor_id,investor_name");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 2);
        Assert.Contains("investor_id", lines[0]);
        Assert.Contains("INV001", lines[1]);
    }

    [Fact]
    public async Task QueryCsv_Allocations_FiltersByInvestor()
    {
        var result = await QueryCsvTool.QueryCsvAsync("allocations", """{"investor_id":"INV001"}""", "deal_id,investor_id");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 2);
        foreach (var line in lines.Skip(1))
            Assert.Contains("INV001", line);
    }

    [Fact]
    public async Task QueryCsv_WrongInvestorId_ReturnsAccessError()
    {
        QueryCsvTool.SetSessionInvestorId("INV001");
        var result = await QueryCsvTool.QueryCsvAsync("allocations", """{"investor_id":"INV999"}""", "*");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Contains("Access denied", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueryCsv_CorrectInvestorId_Works()
    {
        QueryCsvTool.SetSessionInvestorId("INV001");
        var result = await QueryCsvTool.QueryCsvAsync("allocations", """{"investor_id":"INV001"}""", "deal_id");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 2);
    }

    [Fact]
    public async Task QueryCsv_TableNotFound_ReturnsError()
    {
        var result = await QueryCsvTool.QueryCsvAsync("nonexistent", "{}", "*");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.StartsWith("Table 'nonexistent' not found", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task QueryCsv_AllColumns_ReturnsAll()
    {
        var result = await QueryCsvTool.QueryCsvAsync("fx_rates", "{}", "*");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.True(lines.Length >= 2);
        Assert.Contains("currency", lines[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Calculate_BasicArithmetic()
    {
        var result = CalcTool.Calculate("2 + 2");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(4.0, json.GetProperty("result").GetDouble());
    }

    [Fact]
    public void Calculate_Multiplication()
    {
        var result = CalcTool.Calculate("450000 / 300000");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.Equal(1.5, json.GetProperty("result").GetDouble());
    }

    [Fact]
    public void Calculate_InvalidExpression_ReturnsError()
    {
        var result = CalcTool.Calculate("hello + world");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(json.TryGetProperty("error", out _));
    }

    [Fact]
    public void ConvertCurrency_USDToGBP()
    {
        var result = CalcTool.ConvertCurrency(100, "USD", "GBP");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(json.TryGetProperty("amount", out _));
        Assert.Equal("GBP", json.GetProperty("currency").GetString());
    }

    [Fact]
    public void ConvertCurrency_UnknownCurrency_ReturnsError()
    {
        var result = CalcTool.ConvertCurrency(100, "XYZ", "GBP");
        var json = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.True(json.TryGetProperty("error", out _));
    }

    [Fact]
    public void SystemPrompt_Exists()
    {
        var content = EvalHelpers.LoadPrompt("InvestorAssistant.Prompts.system.md");
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("Portfolio Overview()", content);
        Assert.Contains("Obligations()", content);
    }

    [Fact]
    public void Templates_Exist()
    {
        var content = EvalHelpers.LoadPrompt("InvestorAssistant.Prompts.templates.md");
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("Response format", content);
    }
}
