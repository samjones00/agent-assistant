using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class StatementAgent : InvestorAgentBase
{
    public StatementAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.statement.md") { }

    public StatementAgent(AIAgent agent) : base(agent) { }

    public override string Category => "statement";
}
