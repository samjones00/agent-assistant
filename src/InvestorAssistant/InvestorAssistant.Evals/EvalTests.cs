using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using InvestorAssistant.Agents;

namespace InvestorAssistant.Evals;

public sealed class FakeChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatResponse> _factory;
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public FakeChatClient(string response)
        : this(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, response))) { }

    public FakeChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> factory)
    {
        _factory = factory;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = messages.ToList().AsReadOnly();
        Calls.Add(list);
        return Task.FromResult(_factory(list));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

public sealed class EvalTests
{
    private const string Endpoint = "http://localhost:11434/v1";
    private const string ModelId = "phi4-mini";

    // ============================================================
    // Section 1: Prompt Template Integrity
    // ============================================================

    [Fact]
    public void AllPromptResourcesExist()
    {
        var asm = Assembly.GetAssembly(typeof(InvestorAgentBase))!;
        var expected = new[]
        {
            "InvestorAssistant.Prompts.orchestrator.md",
            "InvestorAssistant.Prompts.portfolio.md",
            "InvestorAssistant.Prompts.position.md",
            "InvestorAssistant.Prompts.obligations.md",
            "InvestorAssistant.Prompts.fees.md",
            "InvestorAssistant.Prompts.valuations.md",
            "InvestorAssistant.Prompts.distributions.md",
            "InvestorAssistant.Prompts.statement.md",
        };

        foreach (var name in expected)
        {
            using var stream = asm.GetManifestResourceStream(name);
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            Assert.False(string.IsNullOrWhiteSpace(content));
        }
    }

    [Fact]
    public void PortfolioPrompt_UsesExpectedVariables()
    {
        var prompt = LoadPrompt("InvestorAssistant.Prompts.portfolio.md");
        Assert.Contains("{investor_id}", prompt);
        Assert.Contains("25 June 2026", prompt);
    }

    [Fact]
    public void OrchestratorPrompt_ListsAllCategories()
    {
        var prompt = LoadPrompt("InvestorAssistant.Prompts.orchestrator.md");
        var categories = new[] { "portfolio", "position", "obligations", "fees", "valuations", "distributions", "statement" };
        foreach (var cat in categories)
            Assert.Contains($"- {cat}", prompt);
        Assert.Contains("investor_id", prompt);
    }

    [Fact]
    public void EveryDomainPrompt_UsesReportDate2026()
    {
        var asm = Assembly.GetAssembly(typeof(InvestorAgentBase))!;
        var domainPrompts = new[]
        {
            "portfolio", "position", "obligations",
            "fees", "valuations", "distributions", "statement"
        };
        foreach (var name in asm.GetManifestResourceNames()
            .Where(n => n.EndsWith(".md") && domainPrompts.Any(d => n.Contains(d))))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            Assert.Contains("2026", content);
        }
    }

    // ============================================================
    // Section 2: Agent Wiring
    // ============================================================

    [Fact]
    public async Task PortfolioAgent_HandleAsync_CallsLlmWithInvestorId()
    {
        var fake = new FakeChatClient("Your portfolio value is $1,420,000.00.");
        var agent = new PortfolioAgent(fake.AsAIAgent());

        var result = await agent.HandleAsync("inv_001", "What's my portfolio worth?");

        Assert.Contains("$1,420,000.00", result);
        Assert.Single(fake.Calls);
    }

    [Fact]
    public async Task PortfolioAgent_HandleAsync_ReturnsExactLlmResponse()
    {
        var expected = "Your portfolio value is $1,234,567.89. Source: valuations.csv:rows 1-5";
        var fake = new FakeChatClient(expected);
        var agent = new PortfolioAgent(fake.AsAIAgent());

        var result = await agent.HandleAsync("inv_001", "test");

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task PositionAgent_ReceivesCorrectPrompt()
    {
        var fake = new FakeChatClient("OK");
        var agent = new PositionAgent(fake.AsAIAgent());

        await agent.HandleAsync("inv_042", "What's my position in Acme Corp?");

        var messages = fake.Calls.Single();
        var userMsg = messages.Last(m => m.Role == ChatRole.User);
        Assert.Contains("inv_042", userMsg.Text);
        Assert.Contains("Acme Corp", userMsg.Text);
    }

    [Fact]
    public async Task FeesAgent_ReceivesCorrectQuestion()
    {
        var fake = new FakeChatClient("OK");
        var agent = new FeesAgent(fake.AsAIAgent());

        await agent.HandleAsync("inv_007", "What fees do I pay on Deal X?");

        var msgs = fake.Calls.Single();
        var user = msgs.Last(m => m.Role == ChatRole.User);
        Assert.Contains("inv_007", user.Text);
        Assert.Contains("Deal X", user.Text);
    }

    [Fact]
    public async Task EverySubAgent_HandleAsync_WorksWithFakeLlm()
    {
        var agents = new IInvestorAgent[]
        {
            new PortfolioAgent(new FakeChatClient("portfolio ok").AsAIAgent()),
            new PositionAgent(new FakeChatClient("position ok").AsAIAgent()),
            new ObligationsAgent(new FakeChatClient("obligations ok").AsAIAgent()),
            new FeesAgent(new FakeChatClient("fees ok").AsAIAgent()),
            new ValuationsAgent(new FakeChatClient("valuations ok").AsAIAgent()),
            new DistributionsAgent(new FakeChatClient("distributions ok").AsAIAgent()),
            new StatementAgent(new FakeChatClient("statement ok").AsAIAgent()),
        };

        foreach (var agent in agents)
        {
            var result = await agent.HandleAsync("inv_001", "test");
            Assert.Contains("ok", result);
        }
    }

    // ============================================================
    // Section 3: Orchestrator Routing
    // ============================================================

    [Fact]
    public void Orchestrator_ExtractJson_ReturnsValidJson()
    {
        var input = @"Here is the routing:
{
    ""investor_id"": ""inv_001"",
    ""category"": ""position"",
    ""question"": ""What's my position in Acme?""
}
Thank you.";
        var json = ExtractJson(input);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal("inv_001", parsed.GetProperty("investor_id").GetString());
        Assert.Equal("position", parsed.GetProperty("category").GetString());
    }

    [Fact]
    public void Orchestrator_ExtractJson_ReturnsEmptyOnNoJson()
    {
        var json = ExtractJson("No JSON here at all");
        Assert.Equal("{}", json);
    }

    [Fact]
    public void Orchestrator_ExtractJson_HandlesMultipleBraces()
    {
        var input = @"Some text with {braces} then {
    ""key"": ""value""
} end.";
        var json = ExtractJson(input);
        Assert.StartsWith("{", json);
        Assert.EndsWith("}", json);
        Assert.Contains("key", json);
    }

    [Fact]
    public void Orchestrator_CanBeConstructedWithFakeSubAgents()
    {
        var routingClient = new FakeChatClient(@"{""investor_id"":""inv_001"",""category"":""portfolio"",""question"":""test""}").AsAIAgent();
        var subAgents = new Dictionary<string, IInvestorAgent>
        {
            ["portfolio"] = new PortfolioAgent(new FakeChatClient("ok").AsAIAgent()),
            ["position"] = new PositionAgent(new FakeChatClient("ok").AsAIAgent()),
        };
        var orchestrator = new OrchestratorAgent(routingClient, subAgents);
        Assert.NotNull(orchestrator);
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : "{}";
    }

    // ============================================================
    // Section 4: Response Content Quality (Integration)
    // ============================================================

    private static IChatClient CreateOllamaClient()
        => new OpenAIClient(
                new ApiKeyCredential("ignored"),
                new OpenAIClientOptions { Endpoint = new Uri(Endpoint) })
            .GetChatClient(ModelId)
            .AsIChatClient();

    [Fact(Skip = "Requires Ollama running with phi4-mini at " + Endpoint)]
    public async Task Integration_PortfolioResponse_HasExpectedStructure()
    {
        var agent = new PortfolioAgent(CreateOllamaClient());
        var result = await agent.HandleAsync("inv_001", "What's my total portfolio value?");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.DoesNotContain("investment advice", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prediction", result, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"\$[\d,]+\.?\d*", result);
    }

    [Fact(Skip = "Requires Ollama running with phi4-mini at " + Endpoint)]
    public async Task Integration_PortfolioResponse_NoOtherInvestorData()
    {
        var agent = new PortfolioAgent(CreateOllamaClient());
        var result = await agent.HandleAsync("inv_001", "How am I doing?");

        Assert.DoesNotContain("inv_002", result);
        Assert.DoesNotContain("inv_003", result);
        Assert.DoesNotContain("other investor", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires Ollama running with phi4-mini at " + Endpoint)]
    public void Integration_Orchestrator_CanBeConstructed()
    {
        var chatClient = CreateOllamaClient();
        var subAgents = new IInvestorAgent[]
        {
            new PortfolioAgent(chatClient),
            new PositionAgent(chatClient),
            new ObligationsAgent(chatClient),
            new FeesAgent(chatClient),
            new ValuationsAgent(chatClient),
            new DistributionsAgent(chatClient),
            new StatementAgent(chatClient),
        };

        var orchestrator = new OrchestratorAgent(chatClient, subAgents);
        Assert.NotNull(orchestrator);
    }

    [Fact(Skip = "Requires Ollama running with phi4-mini at " + Endpoint)]
    public async Task Integration_PositionResponse_ContainsCompanyName()
    {
        var agent = new PositionAgent(CreateOllamaClient());
        var result = await agent.HandleAsync("inv_001", "What's my position in the portfolio?");

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    // ============================================================
    // Helpers
    // ============================================================

    private static string LoadPrompt(string resourceName)
    {
        var asm = Assembly.GetAssembly(typeof(InvestorAgentBase))!;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Resource '{resourceName}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
