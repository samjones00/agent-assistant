using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Tools;

public static class QueryCsvTool
{
    private static string? _dataDirectory;
    private static string? _sessionInvestorId;

    public static string? DataDirectory => _dataDirectory;
    public static string? SessionInvestorId => _sessionInvestorId;

    public static void Configure(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
    }

    public static void SetSessionInvestorId(string investorId)
    {
        _sessionInvestorId = investorId;
    }

    private static string NormalizeInvestorId(string id)
    {
        if (id.StartsWith("INV", StringComparison.OrdinalIgnoreCase))
            return id.ToUpperInvariant();
        if (int.TryParse(id, out var num))
            return $"INV{num:D3}";
        return id;
    }

    [Description("Queries CSV data. table: CSV filename without extension (e.g. allocations, deals, investors). filters: JSON like {\"investor_id\":\"INV001\"} to filter rows. columns: comma-separated column names or \"*\" for all. Returns JSON.")]
    public static async Task<string> QueryCsvAsync(
        [Description("Table name (e.g. allocations, deals, investors, valuations, capital_calls, fees, distributions, statement_lines, portfolio_companies, fx_rates)")] string table,
        [Description("JSON filter string like {\"investor_id\":\"INV001\"}. Use empty string \"{}\" for no filter.")] string filters,
        [Description("Comma-separated columns to return, or \"*\" for all columns")] string columns)
    {
        if (_dataDirectory == null)
            return JsonSerializer.Serialize(new { error = "Data directory not configured" });

        var path = Path.Combine(_dataDirectory, $"{table}.csv");
        if (!File.Exists(path))
            return JsonSerializer.Serialize(new { error = $"Table '{table}' not found. Available: investors, portfolio_companies, deals, allocations, valuations, capital_calls, fees, distributions, statement_lines, fx_rates" });

        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2)
            return JsonSerializer.Serialize(Array.Empty<object>());

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        var filterDict = string.IsNullOrWhiteSpace(filters) || filters == "{}"
            ? new Dictionary<string, string>()
            : ParseFilters(filters);

        if (_sessionInvestorId != null && filterDict.TryGetValue("investor_id", out var requestedId) && requestedId != _sessionInvestorId)
            return JsonSerializer.Serialize(new { error = $"Access denied: investor_id '{requestedId}' does not match the logged-in investor '{_sessionInvestorId}'. You can only query data for your own investor ID." });

        var colList = columns == "*"
            ? headers.ToList()
            : columns.Split(',').Select(c => c.Trim()).Where(c => headers.Contains(c)).ToList();

        var rows = new List<Dictionary<string, object>>();

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var values = ParseCsvLine(lines[i], headers.Length);
            var row = new Dictionary<string, object>();
            for (int j = 0; j < headers.Length && j < values.Length; j++)
                row[headers[j]] = values[j];

            var match = filterDict.All(f =>
                row.TryGetValue(f.Key, out var v) && v?.ToString() == f.Value);

            if (match)
            {
                var projected = colList.ToDictionary(c => c, c => row.GetValueOrDefault(c, ""));
                rows.Add(projected);
            }
        }

        if (rows.Count == 0)
            return "No data found.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(" | ", colList));
        foreach (var row in rows)
            sb.AppendLine(string.Join(" | ", colList.Select(c => row.GetValueOrDefault(c, "")?.ToString() ?? "")));

        if (rows.Count > 20)
            sb.AppendLine($"(Showing 20 of {rows.Count} rows)");

        return sb.ToString().TrimEnd();
    }

    private static string[] ParseCsvLine(string line, int expectedCount)
    {
        var result = new List<string>();
        var current = "";
        var inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current += '"';
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (line[i] == ',' && !inQuotes)
            {
                result.Add(current.Trim());
                current = "";
            }
            else
            {
                current += line[i];
            }
        }
        result.Add(current.Trim());

        while (result.Count < expectedCount)
            result.Add("");

        return result.ToArray();
    }

    private static Dictionary<string, string> ParseFilters(string filters)
    {
        // Try JSON first: {"investor_id":"INV001"}
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(filters);
            if (dict != null) return dict;
        }
        catch { }

        // Try SQL-style: investor_id = 'INV001' or investor_id = INV001
        var result = new Dictionary<string, string>();
        var parts = filters.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var eqIdx = part.IndexOf('=');
            if (eqIdx < 0) continue;
            var key = part[..eqIdx].Trim().Trim('"');
            var val = part[(eqIdx + 1)..].Trim().Trim('\'').Trim('"');
            result[key] = val;
        }
        return result;
    }
}
