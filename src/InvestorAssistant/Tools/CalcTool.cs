using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace InvestorAssistant.Tools;

public static class CalcTool
{
    private static Dictionary<string, double>? _fxRates;

    public static void LoadFxRates(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "fx_rates.csv");
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return;
        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        _fxRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = lines[i].Split(',');
            if (values.Length >= 2 && double.TryParse(values[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                _fxRates[values[0].Trim().ToUpperInvariant()] = rate;
        }
    }

    [Description("Evaluates a safe arithmetic expression. Use for MOIC, sums, percentages. Supports +, -, *, /, parentheses.")]
    public static string Calculate(
        [Description("Arithmetic expression, e.g. \"450000 / 300000\" or \"(1500000 + 800000) / 2000000\"")] string expression)
    {
        try
        {
            var sanitized = expression
                .Replace("×", "*")
                .Replace("÷", "/")
                .Replace("—", "-")
                .Replace("–", "-");

            var table = new DataTable();
            var result = table.Compute(sanitized, "");

            if (result is DBNull || result == null)
                return JsonSerializer.Serialize(new { error = "Expression returned no result" });

            var d = Convert.ToDouble(result, CultureInfo.InvariantCulture);
            return JsonSerializer.Serialize(new { result = Math.Round(d, 10) });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Invalid expression: {ex.Message}" });
        }
    }

    [Description("Converts an amount from one currency to another using fx_rates.csv. Example: ConvertCurrency(100, \"GBP\", \"USD\") or ConvertCurrency(135000, \"USD\", \"GBP\")")]
    public static string ConvertCurrency(
        [Description("Amount to convert")] double amount,
        [Description("Source currency code, e.g. USD, GBP, EUR, AED")] string fromCurrency,
        [Description("Target currency code, e.g. USD, GBP, EUR, AED")] string toCurrency)
    {
        if (_fxRates == null)
            return JsonSerializer.Serialize(new { error = "FX rates not loaded" });

        var from = fromCurrency.Trim().ToUpperInvariant();
        var to = toCurrency.Trim().ToUpperInvariant();

        if (!_fxRates.TryGetValue(from, out var fromRate))
            return JsonSerializer.Serialize(new { error = $"Unknown currency: {fromCurrency}" });
        if (!_fxRates.TryGetValue(to, out var toRate))
            return JsonSerializer.Serialize(new { error = $"Unknown currency: {toCurrency}" });

        var result = amount * fromRate / toRate;
        return JsonSerializer.Serialize(new { amount = Math.Round(result, 2), currency = toCurrency });
    }

    public static double ConvertValue(double amount, string fromCurrency, string toCurrency)
    {
        if (_fxRates == null) return amount;

        var from = fromCurrency.Trim().ToUpperInvariant();
        var to = toCurrency.Trim().ToUpperInvariant();

        if (from == to || !_fxRates.TryGetValue(from, out var fromRate) || !_fxRates.TryGetValue(to, out var toRate))
            return amount;

        return amount * fromRate / toRate;
    }
}
