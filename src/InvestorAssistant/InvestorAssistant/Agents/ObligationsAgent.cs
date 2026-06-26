using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class ObligationsAgent : InvestorAgentBase
{
    public ObligationsAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.obligations.md") { }

    public ObligationsAgent(AIAgent agent) : base(agent) { }

    public override string Category => "obligations";
}
