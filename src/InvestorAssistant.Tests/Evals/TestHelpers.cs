using InvestorAssistant.Tools;

namespace InvestorAssistant.Tests.Evals;

internal static class TestHelpers
{
    public static string GetDataDir()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var primary = Path.Combine(repoRoot, "src", "InvestorAssistant", "Data");
        if (Directory.Exists(primary))
            return primary;

        throw new DirectoryNotFoundException("Unable to locate data directory for evaluations.");
    }

    public static string LoadPrompt(string resourceName)
    {
        var asm = typeof(QueryCsvTool).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Resource '{resourceName}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
