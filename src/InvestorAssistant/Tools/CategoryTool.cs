using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace InvestorAssistant.Tools;

public static class CategoryTool
{
    [Description("Lists the available query categories the assistant can help with. Returns each category name and a short description.")]
    public static string ListCategories()
    {
        var asm = typeof(CategoryTool).Assembly;
        using var stream = asm.GetManifestResourceStream("InvestorAssistant.Prompts.system.md")
            ?? throw new FileNotFoundException("Embedded resource 'system.md' not found.");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        var categories = ParseCategories(content);
        return JsonSerializer.Serialize(categories);
    }

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
