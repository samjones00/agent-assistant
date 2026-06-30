using System.Reflection;

namespace InvestorAssistant.Tools;

public static class CategoryTool
{
    public static (string Name, string Description)[] ParseCategories(string markdown)
    {
        var lines = markdown.Split('\n');
        var result = new List<(string, string)>();
        bool inToolsSection = false;
        var skipTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "calculate", "convert_currency" };

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            
            if (trimmed.StartsWith("## Tools"))
            {
                inToolsSection = true;
                continue;
            }
            
            if (inToolsSection && trimmed.StartsWith("## "))
                break;
            
            if (inToolsSection && trimmed.StartsWith("- "))
            {
                var content = trimmed[2..];
                var parenIdx = content.IndexOf('(');
                var descSep = content.IndexOf("): ");
                if (parenIdx > 0 && descSep > parenIdx)
                {
                    var name = content[..parenIdx].Trim();
                    if (skipTools.Contains(name)) continue;
                    var desc = content[(descSep + 3)..].Trim();
                    result.Add((name, desc));
                }
            }
        }

        return result.ToArray();
    }
}
