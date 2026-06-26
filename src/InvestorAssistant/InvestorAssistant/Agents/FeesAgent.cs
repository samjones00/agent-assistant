using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class FeesAgent : InvestorAgentBase
{
    public FeesAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.fees.md") { }

    public FeesAgent(AIAgent agent) : base(agent) { }

    public override string Category => "fees";
}
