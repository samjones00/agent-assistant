using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class DistributionsAgent : InvestorAgentBase
{
    public DistributionsAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.distributions.md") { }

    public DistributionsAgent(AIAgent agent) : base(agent) { }

    public override string Category => "distributions";
}
