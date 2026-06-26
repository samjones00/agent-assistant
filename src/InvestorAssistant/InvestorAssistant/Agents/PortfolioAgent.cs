using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace InvestorAssistant.Agents;

public class PortfolioAgent : InvestorAgentBase
{
    public PortfolioAgent(IChatClient chatClient)
        : base(chatClient, "InvestorAssistant.Prompts.portfolio.md") { }

    public PortfolioAgent(AIAgent agent) : base(agent) { }

    public override string Category => "portfolio";
}
