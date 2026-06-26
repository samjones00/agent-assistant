using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class PositionAgent : InvestorAgentBase
{
    public PositionAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.position.md") { }

    public PositionAgent(AIAgent agent) : base(agent) { }

    public override string Category => "position";
}
