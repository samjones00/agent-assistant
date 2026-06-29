using System.Text.Json;
using InvestorAssistant.Tools;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace InvestorAssistant.Evals;

public static class EvalHelpers
{
    public const string GHEndpoint = "https://models.inference.ai.azure.com";

    public static IChatClient CreateGptClient(string apiKey, string modelId = "gpt-4o-mini")
        => new OpenAIClient(
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(GHEndpoint) })
            .GetChatClient(modelId)
            .AsIChatClient();

    public static string GetDataDir()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
            "EquiTie - Senior Software Engineer - Case Study", "data"));

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
        CalcTool.LoadFxRates(dataDir);
        return [
            AIFunctionFactory.Create(QueryCsvTool.QueryCsvAsync, "query_csv"),
            AIFunctionFactory.Create(CalcTool.Calculate, "calculate"),
            AIFunctionFactory.Create(CalcTool.ConvertCurrency, "convert_currency"),
            AIFunctionFactory.Create(CategoryTool.ListCategories, "list_categories"),
        ];
    }

    public static async Task<string> RunToolLoop(IChatClient client, List<ChatMessage> messages, ChatOptions options, int maxTurns = 10)
    {
        for (int turn = 0; turn < maxTurns; turn++)
        {
            var response = await client.GetResponseAsync(messages, options);
            var msg = response.Messages.Last();
            messages.Add(msg);

            var toolCalls = msg.Contents.OfType<FunctionCallContent>().ToList();
            if (toolCalls.Count == 0)
                return msg.Text ?? "";

            foreach (var call in toolCalls)
            {
                try
                {
                    var tool = options.Tools!.OfType<AIFunction>().FirstOrDefault(t => t.Name == call.Name);
                    if (tool == null) continue;
                    var aiArgs = call.Arguments != null
                        ? new AIFunctionArguments(call.Arguments.ToDictionary(k => k.Key, v => v.Value))
                        : null;
                    var result = await tool.InvokeAsync(aiArgs);
                    var resultJson = result is string s ? s : JsonSerializer.Serialize(result);
                    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, resultJson)]));
                }
                catch { }
            }
        }
        return messages.Last().Text ?? "";
    }

    public static void AssertFormat(string response, string requiredHeader, string requiredMarker)
    {
        Assert.Contains(requiredHeader, response);
        Assert.Contains(requiredMarker, response);
        Assert.DoesNotContain("|---|---|---|", response);
    }
}
