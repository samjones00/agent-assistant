using System.Text.Json;
using InvestorAssistant.Tools;
using Xunit;

namespace InvestorAssistant.Tests.Evals;

public sealed class ToolTests
{
    public ToolTests()
    {
        var dataDir = TestHelpers.GetDataDir();
        QueryCsvTool.Configure(dataDir);
        ColumnMappings.Load(dataDir);
        CalcTool.LoadFxRates(dataDir);
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
        var content = TestHelpers.LoadPrompt("InvestorAssistant.Prompts.system.md");
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("portfolio_overview()", content);
        Assert.Contains("obligations()", content);
    }

    [Fact]
    public void ResponseFormat_Exists()
    {
        var content = TestHelpers.LoadPrompt("InvestorAssistant.Prompts.system.md");
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("Response format", content);
    }

    // ============================================================
    // PortfolioHelperTool — edge cases
    // ============================================================

    [Fact]
    public async Task GetPortfolioOverview_ZeroHoldings_ReturnsNoInvestments()
    {
        QueryCsvTool.SetSessionReportingCurrency("USD");
        var result = await PortfolioHelperTool.GetPortfolioOverview("INV022");
        Assert.Equal("You have no investments yet.", result);
    }

    [Fact]
    public async Task GetPortfolioOverview_PendingAllocation_ShowsPending()
    {
        QueryCsvTool.SetSessionReportingCurrency("USD");
        var result = await PortfolioHelperTool.GetPortfolioOverview("INV021");
        Assert.Contains("(Pending)", result);
        Assert.Contains("0.00", result);
    }

    [Fact]
    public async Task GetSinglePosition_UnknownCompany_ReturnsNotFound()
    {
        var result = await PortfolioHelperTool.GetSinglePosition("INV001", "NonExistentCompanyXYZ");
        Assert.Contains("No company found", result);
    }

    [Fact]
    public async Task GetPortfolioOverview_IncludesReportingCurrencyHeader()
    {
        QueryCsvTool.SetSessionReportingCurrency("GBP");
        var result = await PortfolioHelperTool.GetPortfolioOverview("INV001");
        Assert.Contains("All amounts in GBP", result);
    }

    [Fact]
    public async Task GetPortfolioOverview_MoicIncludesDistributions()
    {
        QueryCsvTool.SetSessionReportingCurrency("EUR");
        // INV011 is in Helianthe Energy (DEAL007, Exited) — received EUR 127,875 distribution
        // MOIC should be > 0 because distributions contribute to the numerator
        var result = await PortfolioHelperTool.GetPortfolioOverview("INV011");
        Assert.Contains("DEAL007", result);
        var moicLine = result.Split('\n').FirstOrDefault(l => l.Contains("DEAL007"));
        Assert.NotNull(moicLine);
        var parts = moicLine.Split('|');
        Assert.True(parts.Length >= 9, $"Expected at least 9 parts, got {parts.Length}");
        var moicStr = parts[7].Trim(); // 8th column = MOIC
        var moicValue = double.Parse(moicStr.TrimEnd('x'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(moicValue > 0, $"MOIC should be positive, got {moicValue}");
    }

    [Fact]
    public async Task GetPortfolioOverview_WrittenOff_ShowsStatus()
    {
        QueryCsvTool.SetSessionReportingCurrency("USD");
        // INV010 holds Yappio (DEAL008, Written Off)
        var result = await PortfolioHelperTool.GetPortfolioOverview("INV010");
        Assert.Contains("Written Off", result);
    }

    [Fact]
    public async Task GetSinglePosition_PartialSecondary_AdjustsCurrentValue()
    {
        QueryCsvTool.SetSessionReportingCurrency("USD");
        // INV013 holds Tallybook (DEAL020) — 30% secondary sold, 70% unrealised
        var result = await PortfolioHelperTool.GetSinglePosition("INV013", "Tallybook");
        Assert.Contains("Tallybook", result);
        // Current value should reflect 70% unrealised (not full units × price)
        Assert.Contains("DEAL020", result);
    }

    [Fact]
    public async Task GetPortfolioOverview_DownRound_MoicLessThanOne()
    {
        QueryCsvTool.SetSessionReportingCurrency("GBP");
        // INV004 has Qubrium Series B (DEAL010) where current mark (6.2) < entry (10.0)
        var result = await PortfolioHelperTool.GetPortfolioOverview("INV004");
        Assert.Contains("DEAL010", result);
    }
}
