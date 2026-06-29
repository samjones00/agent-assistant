using System.Text.Json;
using InvestorAssistant.Tools;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Tests.Evals;

internal static class EvalHelpers
{
    public static string GetDataDir()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var primary = Path.Combine(repoRoot, "src", "InvestorAssistant", "InvestorAssistant", "Data");
        if (Directory.Exists(primary))
            return primary;

        var legacy = Path.Combine(repoRoot, "EquiTie - Senior Software Engineer - Case Study", "data");
        if (Directory.Exists(legacy))
            return legacy;

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

    public static List<AIFunction> DefaultTools()
    {
        var dataDir = GetDataDir();
        QueryCsvTool.Configure(dataDir);
        ColumnMappings.Load(dataDir);
        CalcTool.LoadFxRates(dataDir);

        return
        [
            AIFunctionFactory.Create(QueryCsvTool.QueryCsvAsync, "query_csv"),
            AIFunctionFactory.Create(CalcTool.Calculate, "calculate"),
            AIFunctionFactory.Create(CalcTool.ConvertCurrency, "convert_currency"),
            AIFunctionFactory.Create(CategoryTool.ListCategories, "list_categories"),
        ];
    }

    public static async Task<string> RunScriptedToolLoop(IEnumerable<ChatMessage> script, List<ChatMessage> messages, ChatOptions options)
    {
        foreach (var assistantMsg in script)
        {
            messages.Add(assistantMsg);

            var toolCalls = assistantMsg.Contents.OfType<FunctionCallContent>().ToList();
            if (toolCalls.Count == 0)
                return assistantMsg.Text ?? string.Empty;

            foreach (var call in toolCalls)
            {
                try
                {
                    var tool = options.Tools?.OfType<AIFunction>().FirstOrDefault(t => t.Name == call.Name);
                    if (tool == null)
                        continue;

                    var aiArgs = call.Arguments != null
                        ? new AIFunctionArguments(call.Arguments.ToDictionary(k => k.Key, v => v.Value))
                        : null;

                    var result = await tool.InvokeAsync(aiArgs);
                    var resultJson = result is string s ? s : JsonSerializer.Serialize(result);
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, resultJson)]));
                }
                catch
                {
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, JsonSerializer.Serialize(new { error = "tool invocation failed" }))]));
                }
            }
        }

        throw new InvalidOperationException("Script did not include a final assistant response without tool calls.");
    }
}
