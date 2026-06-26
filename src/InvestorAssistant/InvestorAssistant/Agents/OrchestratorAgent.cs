using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class OrchestratorAgent
{
    private readonly AIAgent _orchestrator;
    private readonly Dictionary<string, IInvestorAgent> _subAgents;
    private string? _investorId;

    public OrchestratorAgent(IChatClient chatClient, IEnumerable<IInvestorAgent> subAgents)
    {
        var prompt = LoadPrompt("InvestorAssistant.Prompts.orchestrator.md");
        _orchestrator = chatClient.AsAIAgent(instructions: prompt);
        _subAgents = subAgents.ToDictionary(a => a.Category);
    }

    // Test-only constructor
    public OrchestratorAgent(AIAgent orchestrator, Dictionary<string, IInvestorAgent> subAgents)
    {
        _orchestrator = orchestrator;
        _subAgents = subAgents;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("EquiTie Investor Assistant");
        Console.WriteLine("==========================");
        Console.WriteLine();

        while (string.IsNullOrWhiteSpace(_investorId))
        {
            Console.Write("Investor ID: ");
            _investorId = Console.ReadLine()?.Trim();
        }

        Console.WriteLine();

        while (true)
        {
            Console.Write("You: ");
            var question = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(question)) break;

            try
            {
                var route = await ClassifyAsync(question, ct);
                var response = await RouteAsync(route, question, ct);
                Console.WriteLine($"\nAssistant: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nAssistant: I ran into an issue processing your request. ({ex.Message})");
            }

            Console.WriteLine();
        }
    }

    private async Task<RouteResult> ClassifyAsync(string question, CancellationToken ct)
    {
        var response = await _orchestrator.RunAsync(
            $"{{\"investor_id\":\"{_investorId}\",\"question\":\"{question}\"}}",
            cancellationToken: ct);

        var text = response.ToString();
        var json = ExtractJson(text);
        return JsonSerializer.Deserialize<RouteResult>(json)
            ?? new RouteResult { investor_id = _investorId!, category = "portfolio", question = question };
    }

    private async Task<string> RouteAsync(RouteResult route, string originalQuestion, CancellationToken ct)
    {
        if (_subAgents.TryGetValue(route.category, out var agent))
        {
            return await agent.HandleAsync(route.investor_id, route.question, ct);
        }

        return await _subAgents["portfolio"].HandleAsync(route.investor_id, originalQuestion, ct);
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }

    private static string LoadPrompt(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private record RouteResult
    {
        public string investor_id { get; init; } = "";
        public string category { get; init; } = "";
        public string question { get; init; } = "";
    }
}
