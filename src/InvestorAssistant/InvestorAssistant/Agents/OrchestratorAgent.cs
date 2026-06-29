using System.Text.Json;
using InvestorAssistant.Tools;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class OrchestratorAgent
{
    private readonly IChatClient _chatClient;
    private readonly string _systemPrompt;
    private readonly List<AIFunction> _tools;
    private readonly string _dataDirectory;
    private string? _investorId;

    public OrchestratorAgent(IChatClient chatClient, string systemPrompt, List<AIFunction> tools, string dataDirectory)
    {
        _chatClient = chatClient;
        _systemPrompt = systemPrompt;
        _tools = tools;
        _dataDirectory = dataDirectory;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("EquiTie Investor Assistant");
        Console.WriteLine("==========================");
        Console.WriteLine();

        do
        {
            Console.Write("Investor ID: ");
            _investorId = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(_investorId))
                Console.WriteLine("Investor ID is required.");
        } while (string.IsNullOrEmpty(_investorId));

        QueryCsvTool.SetSessionInvestorId(_investorId);

        var profile = await LoadInvestorProfileAsync(_investorId);
        var profileJson = JsonSerializer.Serialize(profile);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, _systemPrompt),
            new(ChatRole.System, $"Current investor profile: {profileJson}")
        };

        Console.WriteLine();
        Console.WriteLine($"Welcome, {profile?["investor_name"] ?? _investorId}!");
        Console.WriteLine("Available query categories:");
        foreach (var (name, desc) in CategoryTool.ParseCategories(_systemPrompt))
            Console.WriteLine($"  \u2022 {name} — {desc}");
        Console.WriteLine();

        while (true)
        {
            Console.Write("You: ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question)) break;

            try
            {
                messages.Add(new(ChatRole.User, question));
                var response = await SendWithToolLoopAsync(messages, ct);
                Console.WriteLine($"\nAssistant: {response.Text}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nAssistant: I ran into an issue. ({ex.Message})");
            }

            Console.WriteLine();
        }
    }

    private async Task<ChatMessage> SendWithToolLoopAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        var options = new ChatOptions { Tools = [.. _tools], Temperature = 0f };

        while (true)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(120));
            try
            {
                var response = await _chatClient.GetResponseAsync(messages, options, timeoutCts.Token);
                var assistantMsg = response.Messages.Last();
                messages.Add(assistantMsg);

                var toolCalls = assistantMsg.Contents.OfType<FunctionCallContent>().ToList();
                if (toolCalls.Count == 0)
                    return assistantMsg;

                foreach (var call in toolCalls)
                {
                    try
                    {
                        var tool = _tools.FirstOrDefault(t => t.Name == call.Name);
                        if (tool != null)
                        {
                            var aiArgs = call.Arguments != null
                                ? new AIFunctionArguments(call.Arguments.ToDictionary(k => k.Key, v => v.Value))
                                : null;
                            var result = await tool.InvokeAsync(aiArgs, ct);
                            var resultJson = result is string s ? s : JsonSerializer.Serialize(result);
                            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, resultJson)]));
                        }
                        else
                        {
                            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, JsonSerializer.Serialize(new { error = $"Tool '{call.Name}' not found" }))]));
                        }
                    }
                    catch (Exception ex)
                    {
                        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, JsonSerializer.Serialize(new { error = ex.Message }))]));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                messages.Add(new ChatMessage(ChatRole.Assistant, "I'm sorry, the request timed out. Please try again."));
                return messages.Last();
            }
        }
    }

    private async Task<Dictionary<string, object>?> LoadInvestorProfileAsync(string investorId)
    {
        var path = Path.Combine(_dataDirectory, "investors.csv");
        if (!File.Exists(path)) return null;

        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length < 2) return null;

        var headers = lines[0].Split(',').Select(h => h.Trim()).ToArray();
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var values = lines[i].Split(',').Select(v => v.Trim().Trim('"')).ToArray();
            if (values.Length >= 1 && values[0] == investorId)
            {
                var profile = new Dictionary<string, object>();
                for (int j = 0; j < headers.Length && j < values.Length; j++)
                    profile[headers[j]] = values[j];
                return profile;
            }
        }
        return null;
    }
}
