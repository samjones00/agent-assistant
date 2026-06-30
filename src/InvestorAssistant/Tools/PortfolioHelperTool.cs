using System.Globalization;

namespace InvestorAssistant.Tools;

public static class PortfolioHelperTool
{
    private static double ConvertCurrencyAmount(double amount, string fromCurrency, string toCurrency)
        => CalcTool.ConvertValue(amount, fromCurrency, toCurrency);
    public static Task<string> GetPortfolioOverviewDirect() =>
        GetPortfolioOverview(QueryCsvTool.SessionInvestorId ?? "");

    public static Task<string> GetSinglePositionDirect(string companyName) =>
        GetSinglePosition(QueryCsvTool.SessionInvestorId ?? "", companyName);

    public static Task<string> GetDistributionsDirect() =>
        GetDistributions(QueryCsvTool.SessionInvestorId ?? "");

    public static Task<string> GetObligationsDirect() =>
        GetObligations(QueryCsvTool.SessionInvestorId ?? "");

    public static Task<string> GetValuationsDirect() =>
        GetValuations(QueryCsvTool.SessionInvestorId ?? "");

    public static Task<string> GetAccountStatementDirect() =>
        GetAccountStatement(QueryCsvTool.SessionInvestorId ?? "");

    public static Task<string> GetFeesDirect(string companyName) =>
        GetFees(QueryCsvTool.SessionInvestorId ?? "", companyName);

    public static async Task<string> GetPortfolioOverview(string investorId)
    {
        var reportingCurrency = QueryCsvTool.SessionReportingCurrency ?? "USD";

        var allocPath = Path.Combine(QueryCsvTool.DataDirectory!, "allocations.csv");
        var dealsPath = Path.Combine(QueryCsvTool.DataDirectory!, "deals.csv");
        var compPath = Path.Combine(QueryCsvTool.DataDirectory!, "portfolio_companies.csv");
        var valPath = Path.Combine(QueryCsvTool.DataDirectory!, "valuations.csv");
        var distPath = Path.Combine(QueryCsvTool.DataDirectory!, "distributions.csv");

        var allocLines = await File.ReadAllLinesAsync(allocPath);
        var dealLines = await File.ReadAllLinesAsync(dealsPath);
        var compLines = await File.ReadAllLinesAsync(compPath);
        var valLines = await File.ReadAllLinesAsync(valPath);
        var distLines = await File.ReadAllLinesAsync(distPath);

        var dealHeaders = dealLines[0].Split(',');
        var dealRows = dealLines.Skip(1).Select(l => ParseRow(l, dealHeaders)).ToList();
        var compHeaders = compLines[0].Split(',');
        var compRows = compLines.Skip(1).Select(l => ParseRow(l, compHeaders)).ToList();
        var valHeaders = valLines[0].Split(',');
        var valRows = valLines.Skip(1).Select(l => ParseRow(l, valHeaders)).ToList();

        var allocHeaders = allocLines[0].Split(',');
        var allocRows = allocLines.Skip(1)
            .Select(l => ParseRow(l, allocHeaders))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId)
            .ToList();

        if (allocRows.Count == 0)
            return "You have no investments yet.";

        var distHeaders = distLines[0].Split(',');
        var distRows = distLines.Skip(1).Select(l => ParseRow(l, distHeaders))
            .Where(d => d.GetValueOrDefault("investor_id", "") == investorId).ToList();

        var distByDeal = distRows
            .GroupBy(d => d.GetValueOrDefault("deal_id", ""))
            .ToDictionary(g => g.Key, g =>
            {
                var net = 0.0;
                var fraction = 0.0;
                foreach (var d in g)
                {
                    _ = double.TryParse(d.GetValueOrDefault("net_amount", ""), out var n);
                    net += n;
                    _ = double.TryParse(d.GetValueOrDefault("fraction_of_units", ""), out var f);
                    fraction += f;
                }
                return (net, fraction);
            });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"All amounts in {reportingCurrency}");
        sb.AppendLine(string.Join(" | ", ColumnMappings.GetTableDisplayNames("portfolio_overview")));

        foreach (var alloc in allocRows)
        {
            var dealId = alloc.GetValueOrDefault("deal_id", "");
            var units = double.TryParse(alloc.GetValueOrDefault("units", ""), out var u) ? u : 0;
            var contributed = double.TryParse(alloc.GetValueOrDefault("contributed_amount", ""), out var c) ? c : 0;
            var commitment = double.TryParse(alloc.GetValueOrDefault("commitment_amount", ""), out var comm) ? comm : 0;
            var status = alloc.GetValueOrDefault("allocation_status", "");
            var allocCurrency = alloc.GetValueOrDefault("deal_currency", "USD");

            var deal = dealRows.FirstOrDefault(d => d.GetValueOrDefault("deal_id", "") == dealId);
            var companyId = deal?.GetValueOrDefault("company_id", "") ?? "";
            var round = deal?.GetValueOrDefault("round", "") ?? "";
            var dealStatus = deal?.GetValueOrDefault("status", "") ?? "";
            var company = compRows.FirstOrDefault(cp => cp.GetValueOrDefault("company_id", "") == companyId);
            var companyName = company?.GetValueOrDefault("company_name", "") ?? companyId;

            var latestVal = valRows.Where(v => v.GetValueOrDefault("deal_id", "") == dealId)
                .OrderByDescending(v => v.GetValueOrDefault("valuation_date", ""))
                .FirstOrDefault();
            var latestPrice = double.TryParse(latestVal?.GetValueOrDefault("share_price", ""), out var lp) ? lp : 0;
            var markSource = latestVal?.GetValueOrDefault("mark_source", "") ?? "";
            var valDate = latestVal?.GetValueOrDefault("valuation_date", "") ?? "";

            var (distNet, distFraction) = distByDeal.GetValueOrDefault(dealId, (0, 0));
            var unrealisedFraction = Math.Max(0, 1 - distFraction);
            var currentValue = units * latestPrice * unrealisedFraction;
            var contributedConv = ConvertCurrencyAmount(contributed, allocCurrency, reportingCurrency);
            var commitmentConv = ConvertCurrencyAmount(commitment, allocCurrency, reportingCurrency);
            var currentValueConv = ConvertCurrencyAmount(currentValue, allocCurrency, reportingCurrency);
            var distNetConv = ConvertCurrencyAmount(distNet, allocCurrency, reportingCurrency);

            var realised = status == "Pending" || contributed == 0;
            var writtenOff = dealStatus == "Written Off" || (latestPrice == 0 && markSource == "Write Off");
            var moic = contributedConv > 0 ? (currentValueConv + distNetConv) / contributedConv : 0;

            var statusTag = realised ? " (Pending)" : writtenOff ? " (Written Off)" : "";
            sb.AppendLine($"{dealId} | {companyName}{statusTag} | {round} | {commitmentConv:F2} | {units:F2} | {contributedConv:F2} | {currentValueConv:F2} | {moic:F2}x | {latestPrice} | {valDate}");
        }

        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetSinglePosition(string investorId, string companyName)
    {
        var reportingCurrency = QueryCsvTool.SessionReportingCurrency ?? "USD";

        var allocPath = Path.Combine(QueryCsvTool.DataDirectory!, "allocations.csv");
        var dealsPath = Path.Combine(QueryCsvTool.DataDirectory!, "deals.csv");
        var compPath = Path.Combine(QueryCsvTool.DataDirectory!, "portfolio_companies.csv");
        var valPath = Path.Combine(QueryCsvTool.DataDirectory!, "valuations.csv");
        var distPath = Path.Combine(QueryCsvTool.DataDirectory!, "distributions.csv");

        var dealLines = await File.ReadAllLinesAsync(dealsPath);
        var compLines = await File.ReadAllLinesAsync(compPath);
        var valLines = await File.ReadAllLinesAsync(valPath);
        var allocLines = await File.ReadAllLinesAsync(allocPath);
        var distLines = await File.ReadAllLinesAsync(distPath);

        var dealHeaders = dealLines[0].Split(',');
        var dealRows = dealLines.Skip(1).Select(l => ParseRow(l, dealHeaders)).ToList();
        var compHeaders = compLines[0].Split(',');
        var compRows = compLines.Skip(1).Select(l => ParseRow(l, compHeaders)).ToList();
        var valHeaders = valLines[0].Split(',');
        var valRows = valLines.Skip(1).Select(l => ParseRow(l, valHeaders)).ToList();
        var allocHeaders = allocLines[0].Split(',');
        var allocRows = allocLines.Skip(1).Select(l => ParseRow(l, allocHeaders)).ToList();
        var distHeaders = distLines[0].Split(',');
        var distRows = distLines.Skip(1).Select(l => ParseRow(l, distHeaders))
            .Where(d => d.GetValueOrDefault("investor_id", "") == investorId).ToList();

        var matchCompany = compRows.FirstOrDefault(c =>
            c.GetValueOrDefault("company_name", "").Contains(companyName, StringComparison.OrdinalIgnoreCase));
        if (matchCompany == null) return $"No company found matching '{companyName}'.";

        var companyId = matchCompany.GetValueOrDefault("company_id", "");
        var deals = dealRows.Where(d => d.GetValueOrDefault("company_id", "") == companyId).ToList();
        if (deals.Count == 0) return $"No deals found for {companyName}.";

        var distByDeal = distRows
            .GroupBy(d => d.GetValueOrDefault("deal_id", ""))
            .ToDictionary(g => g.Key, g =>
            {
                var net = 0.0;
                var fraction = 0.0;
                foreach (var d in g)
                {
                    _ = double.TryParse(d.GetValueOrDefault("net_amount", ""), out var n);
                    net += n;
                    _ = double.TryParse(d.GetValueOrDefault("fraction_of_units", ""), out var f);
                    fraction += f;
                }
                return (net, fraction);
            });

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Position in {matchCompany.GetValueOrDefault("company_name", "")} (all amounts in {reportingCurrency})");
        sb.AppendLine(string.Join(" | ", ColumnMappings.GetTableDisplayNames("single_position")));

        foreach (var deal in deals)
        {
            var dealId = deal.GetValueOrDefault("deal_id", "");
            var round = deal.GetValueOrDefault("round", "");
            var alloc = allocRows.FirstOrDefault(a =>
                a.GetValueOrDefault("deal_id", "") == dealId &&
                a.GetValueOrDefault("investor_id", "") == investorId);
            if (alloc == null) continue;

            var units = double.TryParse(alloc.GetValueOrDefault("units", ""), out var u) ? u : 0;
            var contributed = double.TryParse(alloc.GetValueOrDefault("contributed_amount", ""), out var c) ? c : 0;
            var status = alloc.GetValueOrDefault("allocation_status", "");
            var allocCurrency = alloc.GetValueOrDefault("deal_currency", "USD");

            var latestVal = valRows.Where(v => v.GetValueOrDefault("deal_id", "") == dealId)
                .OrderByDescending(v => v.GetValueOrDefault("valuation_date", ""))
                .FirstOrDefault();
            var latestPrice = double.TryParse(latestVal?.GetValueOrDefault("share_price", ""), out var lp) ? lp : 0;
            var markSource = latestVal?.GetValueOrDefault("mark_source", "") ?? "";
            var valDate = latestVal?.GetValueOrDefault("valuation_date", "") ?? "";

            var (distNet, distFraction) = distByDeal.GetValueOrDefault(dealId, (0, 0));
            var unrealisedFraction = Math.Max(0, 1 - distFraction);
            var currentValue = units * latestPrice * unrealisedFraction;
            var contributedConv = ConvertCurrencyAmount(contributed, allocCurrency, reportingCurrency);
            var currentValueConv = ConvertCurrencyAmount(currentValue, allocCurrency, reportingCurrency);
            var distNetConv = ConvertCurrencyAmount(distNet, allocCurrency, reportingCurrency);

            var dealStatus = deal.GetValueOrDefault("status", "");
            var writtenOff = dealStatus == "Written Off" || (latestPrice == 0 && markSource == "Write Off");
            var moic = contributedConv > 0 ? (currentValueConv + distNetConv) / contributedConv : 0;

            var statusTag = status == "Pending" ? " (Pending)" : writtenOff ? " (Written Off)" : "";
            sb.AppendLine($"{dealId} | {round}{statusTag} | {units:F2} | {contributedConv:F2} | {currentValueConv:F2} | {moic:F2}x | {latestPrice} | {valDate}");
        }

        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetDistributions(string investorId)
    {
        var path = Path.Combine(QueryCsvTool.DataDirectory!, "distributions.csv");
        if (!File.Exists(path)) return "No distributions data found.";

        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2) return "No distributions found.";

        var headers = lines[0].Split(',');
        var rows = lines.Skip(1).Select(l => ParseRow(l, headers))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId)
            .ToList();

        if (rows.Count == 0) return "No distributions found.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(" | ", ColumnMappings.GetDisplayNames(headers)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(" | ", headers.Select(h => row.GetValueOrDefault(h, ""))));

        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetObligations(string investorId)
    {
        var callsPath = Path.Combine(QueryCsvTool.DataDirectory!, "capital_calls.csv");
        var feesPath = Path.Combine(QueryCsvTool.DataDirectory!, "fees.csv");

        var callsLines = await File.ReadAllLinesAsync(callsPath);
        var feesLines = await File.ReadAllLinesAsync(feesPath);

        var callsHeaders = callsLines[0].Split(',');
        var callsRows = callsLines.Skip(1).Select(l => ParseRow(l, callsHeaders))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId)
            .ToList();

        var feesHeaders = feesLines[0].Split(',');
        var feesRows = feesLines.Skip(1).Select(l => ParseRow(l, feesHeaders))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId)
            .ToList();

        var sb = new System.Text.StringBuilder();

        if (callsRows.Count > 0)
        {
            sb.AppendLine("Capital Calls:");
            sb.AppendLine(string.Join(" | ", ColumnMappings.GetDisplayNames(callsHeaders)));
            foreach (var row in callsRows)
                sb.AppendLine(string.Join(" | ", callsHeaders.Select(h => row.GetValueOrDefault(h, ""))));
        }

        if (feesRows.Count > 0)
        {
            if (callsRows.Count > 0) sb.AppendLine();
            sb.AppendLine("Fees:");
            sb.AppendLine(string.Join(" | ", ColumnMappings.GetDisplayNames(feesHeaders)));
            foreach (var row in feesRows)
                sb.AppendLine(string.Join(" | ", feesHeaders.Select(h => row.GetValueOrDefault(h, ""))));
        }

        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetFees(string investorId, string companyName)
    {
        var feesPath = Path.Combine(QueryCsvTool.DataDirectory!, "fees.csv");
        var allocPath = Path.Combine(QueryCsvTool.DataDirectory!, "allocations.csv");
        var dealsPath = Path.Combine(QueryCsvTool.DataDirectory!, "deals.csv");
        var compPath = Path.Combine(QueryCsvTool.DataDirectory!, "portfolio_companies.csv");

        var feesLines = await File.ReadAllLinesAsync(feesPath);
        var allocLines = await File.ReadAllLinesAsync(allocPath);
        var dealLines = await File.ReadAllLinesAsync(dealsPath);
        var compLines = await File.ReadAllLinesAsync(compPath);

        var allocHeaders = allocLines[0].Split(',');
        var allocRows = allocLines.Skip(1).Select(l => ParseRow(l, allocHeaders))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId).ToList();
        var dealHeaders = dealLines[0].Split(',');
        var dealRows = dealLines.Skip(1).Select(l => ParseRow(l, dealHeaders)).ToList();
        var compHeaders = compLines[0].Split(',');
        var compRows = compLines.Skip(1).Select(l => ParseRow(l, compHeaders)).ToList();

        var matchingDealIds = new HashSet<string>();
        foreach (var alloc in allocRows)
        {
            var dealId = alloc.GetValueOrDefault("deal_id", "");
            var deal = dealRows.FirstOrDefault(d => d.GetValueOrDefault("deal_id", "") == dealId);
            var companyId = deal?.GetValueOrDefault("company_id", "") ?? "";
            var comp = compRows.FirstOrDefault(c => c.GetValueOrDefault("company_id", "") == companyId);
            if (comp != null && comp.GetValueOrDefault("company_name", "")
                .Contains(companyName, StringComparison.OrdinalIgnoreCase))
                matchingDealIds.Add(dealId);
        }

        var feesHeaders = feesLines[0].Split(',');
        var feesRows = feesLines.Skip(1).Select(l => ParseRow(l, feesHeaders))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId &&
                        matchingDealIds.Contains(r.GetValueOrDefault("deal_id", "")))
            .ToList();

        if (feesRows.Count == 0)
            return $"No fees found for {companyName}.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(" | ", ColumnMappings.GetDisplayNames(feesHeaders)));
        foreach (var row in feesRows)
            sb.AppendLine(string.Join(" | ", feesHeaders.Select(h => row.GetValueOrDefault(h, ""))));

        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetValuations(string investorId)
    {
        var allocPath = Path.Combine(QueryCsvTool.DataDirectory!, "allocations.csv");
        var dealsPath = Path.Combine(QueryCsvTool.DataDirectory!, "deals.csv");
        var compPath = Path.Combine(QueryCsvTool.DataDirectory!, "portfolio_companies.csv");
        var valPath = Path.Combine(QueryCsvTool.DataDirectory!, "valuations.csv");

        var allocLines = await File.ReadAllLinesAsync(allocPath);
        var dealLines = await File.ReadAllLinesAsync(dealsPath);
        var compLines = await File.ReadAllLinesAsync(compPath);
        var valLines = await File.ReadAllLinesAsync(valPath);

        var allocHeaders = allocLines[0].Split(',');
        var allocRows = allocLines.Skip(1).Select(l => ParseRow(l, allocHeaders))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId).ToList();
        var dealHeaders = dealLines[0].Split(',');
        var dealRows = dealLines.Skip(1).Select(l => ParseRow(l, dealHeaders)).ToList();
        var compHeaders = compLines[0].Split(',');
        var compRows = compLines.Skip(1).Select(l => ParseRow(l, compHeaders)).ToList();
        var valHeaders = valLines[0].Split(',');
        var valRows = valLines.Skip(1).Select(l => ParseRow(l, valHeaders)).ToList();

        var dealIds = allocRows.Select(a => a.GetValueOrDefault("deal_id", "")).Distinct().ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(" | ", ColumnMappings.GetTableDisplayNames("valuations")));

        foreach (var dealId in dealIds)
        {
            var deal = dealRows.FirstOrDefault(d => d.GetValueOrDefault("deal_id", "") == dealId);
            var companyId = deal?.GetValueOrDefault("company_id", "") ?? "";
            var company = compRows.FirstOrDefault(c => c.GetValueOrDefault("company_id", "") == companyId);
            var companyName = company?.GetValueOrDefault("company_name", "") ?? companyId;
            var round = deal?.GetValueOrDefault("round", "") ?? "";

            var vals = valRows.Where(v => v.GetValueOrDefault("deal_id", "") == dealId)
                .OrderByDescending(v => v.GetValueOrDefault("valuation_date", ""))
                .ToList();

            foreach (var val in vals)
            {
                var price = val.GetValueOrDefault("share_price", "");
                var date = val.GetValueOrDefault("valuation_date", "");
                var companyVal = val.GetValueOrDefault("company_valuation_m", "");
                sb.AppendLine($"{companyName} | {dealId} | {round} | {price} | {date} | {companyVal}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static async Task<string> GetAccountStatement(string investorId)
    {
        var path = Path.Combine(QueryCsvTool.DataDirectory!, "statement_lines.csv");
        if (!File.Exists(path)) return "No statement data found.";

        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2) return "No statement lines found.";

        var headers = lines[0].Split(',');
        var rows = lines.Skip(1).Select(l => ParseRow(l, headers))
            .Where(r => r.GetValueOrDefault("investor_id", "") == investorId)
            .ToList();

        if (rows.Count == 0) return "No statement lines found.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(" | ", ColumnMappings.GetDisplayNames(headers)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(" | ", headers.Select(h => row.GetValueOrDefault(h, ""))));

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> ParseRow(string line, string[] headers)
    {
        var values = line.Split(',');
        var row = new Dictionary<string, string>();
        for (int i = 0; i < headers.Length && i < values.Length; i++)
            row[headers[i].Trim()] = values[i].Trim().Trim('"');
        return row;
    }
}
