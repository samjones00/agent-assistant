using System.Text.Json;
using InvestorAssistant.Tools;
using Microsoft.Extensions.AI;
using Spectre.Console;

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

    public async Task RunAsync(string? investorId = null, CancellationToken ct = default)
    {
        Console.WriteLine("EquiTie Investor Assistant");
        Console.WriteLine("==========================");
        Console.WriteLine();

        _investorId = investorId;

        Dictionary<string, object>? profile;
        if (!string.IsNullOrEmpty(_investorId))
        {
            Console.WriteLine($"Investor ID: {_investorId}");
            profile = await LoadInvestorProfileAsync(_investorId);
            if (profile == null)
            {
                Console.WriteLine($"Investor '{_investorId}' not found.");
                return;
            }
        }
        else
        {
            profile = null;
            do
                {
                    Console.Write("Investor ID: ");
                    _investorId = Console.ReadLine()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(_investorId))
                    {
                        Console.WriteLine("Investor ID is required.");
                        continue;
                    }
                    profile = await LoadInvestorProfileAsync(_investorId);
                    if (profile == null)
                        Console.WriteLine($"Investor '{_investorId}' not found. Please try again.");
                } while (profile == null);
            }

            QueryCsvTool.SetSessionInvestorId(_investorId);
            var reportingCurrency = profile?.GetValueOrDefault("reporting_currency")?.ToString() ?? "USD";
            QueryCsvTool.SetSessionReportingCurrency(reportingCurrency);
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
                var assistantResult = await SendWithToolLoopAsync(messages, ct);
                Console.WriteLine($"\nAssistant: {assistantResult.Response.Text}");
                RenderToolExecutions(assistantResult.ToolExecutions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nAssistant: I ran into an issue. ({ex.Message})");
            }

            Console.WriteLine();
        }
    }

    private async Task<AssistantResult> SendWithToolLoopAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        var options = new ChatOptions { Tools = [.. _tools], Temperature = 0f };
        var executedTools = new List<ToolExecution>();

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
                    return new AssistantResult(assistantMsg, executedTools);

                var wrapperTools = new[] { "portfolio_overview", "single_position", "distributions", "obligations", "fees", "valuations", "account_statement" };

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
                            var resultStr = result is string s ? s : JsonSerializer.Serialize(result);
                            if (wrapperTools.Contains(call.Name))
                            {
                                var cleanResult = resultStr;
                                try { cleanResult = JsonSerializer.Deserialize<string>(resultStr) ?? resultStr; } catch { }
                                cleanResult = cleanResult.Replace("\r\n", "\n").Replace("\r", "\n");
                                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, cleanResult)]));
                                executedTools.Add(new ToolExecution(call.Name, cleanResult));
                            }
                            else
                            {
                                messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, resultStr)]));
                                executedTools.Add(new ToolExecution(call.Name, resultStr));
                            }
                        }
                        else
                        {
                            messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, JsonSerializer.Serialize(new { error = $"Tool '{call.Name}' not found" }))]));
                            executedTools.Add(new ToolExecution(call.Name, $"Tool '{call.Name}' not found"));
                        }
                    }
                    catch (Exception ex)
                    {
                        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, JsonSerializer.Serialize(new { error = ex.Message }))]));
                        executedTools.Add(new ToolExecution(call.Name, ex.Message));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                var timeoutMessage = new ChatMessage(ChatRole.Assistant, "I'm sorry, the request timed out. Please try again.");
                messages.Add(timeoutMessage);
                return new AssistantResult(timeoutMessage, executedTools);
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

    private void RenderToolExecutions(IReadOnlyList<ToolExecution> executions)
    {
        if (executions.Count == 0)
            return;

        foreach (var execution in executions)
        {
            var sections = ParseSections(execution.Result);
            if (sections.Count == 0)
            {
                AnsiConsole.MarkupLine(Markup.Escape(execution.Result));
                AnsiConsole.WriteLine();
                continue;
            }

            foreach (var section in sections)
            {
                switch (section)
                {
                    case TableSection tableSection:
                        if (!string.IsNullOrEmpty(tableSection.Title))
                            AnsiConsole.MarkupLine($"[bold]{Markup.Escape(tableSection.Title!)}[/]");

                        var table = new Table().RoundedBorder().Expand();
                        foreach (var header in tableSection.Headers)
                            table.AddColumn(new TableColumn(Markup.Escape(header)) { Padding = new Padding(1, 0, 1, 0) });

                        foreach (var row in tableSection.Rows)
                            table.AddRow(row.Select(Markup.Escape).ToArray());

                        AnsiConsole.Write(table);
                        AnsiConsole.WriteLine();
                        break;

                    case TextSection textSection:
                        if (!string.IsNullOrWhiteSpace(textSection.Content))
                        {
                            AnsiConsole.MarkupLine(Markup.Escape(textSection.Content));
                            AnsiConsole.WriteLine();
                        }
                        break;
                }
            }
        }
    }

    private static List<ResultSection> ParseSections(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<ResultSection>();

        var normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (string.IsNullOrEmpty(normalized))
            return new List<ResultSection>();

        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sections = new List<ResultSection>();

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
                continue;

            string? title = null;
            int dataStart = 0;

            if (!lines[0].Contains('|'))
            {
                title = lines[0].TrimEnd(':');
                dataStart = 1;
            }

            if (dataStart >= lines.Length)
            {
                sections.Add(new TextSection(block.Trim()));
                continue;
            }

            var headerLine = lines[dataStart];
            if (!headerLine.Contains('|'))
            {
                sections.Add(new TextSection(block.Trim()));
                continue;
            }

            var headers = SplitRow(headerLine);
            if (headers.Length == 0)
            {
                sections.Add(new TextSection(block.Trim()));
                continue;
            }

            var rows = new List<string[]>();
            var tableValid = true;

            for (int i = dataStart + 1; i < lines.Length; i++)
            {
                if (!lines[i].Contains('|'))
                {
                    tableValid = false;
                    break;
                }

                var cells = SplitRow(lines[i]);
                if (cells.Length != headers.Length)
                {
                    tableValid = false;
                    break;
                }

                rows.Add(cells);
            }

            if (!tableValid || rows.Count == 0)
            {
                sections.Add(new TextSection(block.Trim()));
                continue;
            }

            sections.Add(new TableSection(title, headers, rows));
        }

        return sections;
    }

    private static string[] SplitRow(string line)
    {
        return line.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private sealed record ToolExecution(string ToolName, string Result);

    private abstract record ResultSection;

    private sealed record TableSection(string? Title, string[] Headers, List<string[]> Rows) : ResultSection;

    private sealed record TextSection(string Content) : ResultSection;

    private sealed record AssistantResult(ChatMessage Response, List<ToolExecution> ToolExecutions);
}
