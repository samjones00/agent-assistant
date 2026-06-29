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
        using var stream = asm.GetManifestResourceStream("InvestorAssistant.Prompts.templates.md")
            ?? throw new FileNotFoundException("Embedded resource 'templates.md' not found.");
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        var categories = ParseCategories(content);
        return JsonSerializer.Serialize(categories);
    }

    public static (string Name, string Description)[] ParseCategories(string markdown)
    {
        var lines = markdown.Split('\n');
        var result = new List<(string, string)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("### ")) continue;

            // Skip indented ### (e.g. inside Wrong:/Correct: examples)
            if (lines[i][0] != '#') continue;

            var name = trimmed[3..].Trim();
            if (name is "Response format rules" or "help") continue;

            for (int j = i + 1; j < lines.Length; j++)
            {
                var next = lines[j].Trim();
                if (string.IsNullOrEmpty(next)) continue;
                if (next.StartsWith("### ")) break;
                if (next.StartsWith("User:") || next.StartsWith("Assistant:")) break;

                result.Add((name, next));
                break;
            }
        }

        return result.ToArray();
    }
}
