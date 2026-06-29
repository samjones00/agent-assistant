using System.Text.Json;

namespace InvestorAssistant.Tools;

public static class ColumnMappings
{
    private static Dictionary<string, string>? _columns;
    private static Dictionary<string, string[]>? _tables;

    public static void Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "column_mappings.json");
        if (!File.Exists(path))
        {
            _columns = new Dictionary<string, string>();
            _tables = new Dictionary<string, string[]>();
            return;
        }

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("columns", out var columnsElement))
            _columns = JsonSerializer.Deserialize<Dictionary<string, string>>(columnsElement.GetRawText())
                ?? new Dictionary<string, string>();
        else
            _columns = new Dictionary<string, string>();

        if (root.TryGetProperty("tables", out var tablesElement))
            _tables = JsonSerializer.Deserialize<Dictionary<string, string[]>>(tablesElement.GetRawText())
                ?? new Dictionary<string, string[]>();
        else
            _tables = new Dictionary<string, string[]>();
    }

    public static string GetDisplayName(string csvColumn)
    {
        if (_columns != null && _columns.TryGetValue(csvColumn, out var displayName))
            return displayName;
        return csvColumn;
    }

    public static string[] GetDisplayNames(string[] csvColumns)
    {
        return csvColumns.Select(GetDisplayName).ToArray();
    }

    public static string[] GetTableColumns(string tableName)
    {
        if (_tables != null && _tables.TryGetValue(tableName, out var columns))
            return columns;
        return Array.Empty<string>();
    }

    public static string[] GetTableDisplayNames(string tableName)
    {
        var columns = GetTableColumns(tableName);
        return columns.Select(GetDisplayName).ToArray();
    }
}
