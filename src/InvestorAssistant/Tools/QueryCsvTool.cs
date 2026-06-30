namespace InvestorAssistant.Tools;

public static class QueryCsvTool
{
    private static string? _dataDirectory;
    private static string? _sessionInvestorId;
    private static string? _sessionReportingCurrency;

    public static string? DataDirectory => _dataDirectory;
    public static string? SessionInvestorId => _sessionInvestorId;
    public static string? SessionReportingCurrency => _sessionReportingCurrency;

    public static void Configure(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public static void SetSessionInvestorId(string investorId)
    {
        _sessionInvestorId = investorId;
    }

    public static void SetSessionReportingCurrency(string currency)
    {
        _sessionReportingCurrency = currency;
    }
}
