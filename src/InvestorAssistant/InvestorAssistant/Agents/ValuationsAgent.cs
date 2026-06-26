using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class ValuationsAgent : InvestorAgentBase
{
    public ValuationsAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.valuations.md") { }

    public ValuationsAgent(AIAgent agent) : base(agent) { }

    public override string Category => "valuations";
}
