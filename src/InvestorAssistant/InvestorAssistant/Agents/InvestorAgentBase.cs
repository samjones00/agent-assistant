using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Reflection;

namespace InvestorAssistant.Agents;

public abstract class InvestorAgentBase : IInvestorAgent
{
    private readonly AIAgent _agent;

    protected InvestorAgentBase(IChatClient chatClient, string promptResourceName)
    {
        var prompt = LoadPrompt(promptResourceName);
        _agent = chatClient.AsAIAgent(instructions: prompt);
    }

    // Test-only constructor that allows injecting a pre-built AIAgent
    protected InvestorAgentBase(AIAgent agent)
    {
        _agent = agent;
    }

    public abstract string Category { get; }

    public async Task<string> HandleAsync(string investorId, string question, CancellationToken ct = default)
    {
        var prompt = FormatPrompt(investorId, question);
        var response = await _agent.RunAsync(prompt, cancellationToken: ct);
        return response.ToString();
    }

    protected virtual string FormatPrompt(string investorId, string question)
        => $"Investor: {investorId}\nQuestion: {question}";

    private static string LoadPrompt(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found. " +
                "Ensure the file is included as <EmbeddedResource> in the .csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
