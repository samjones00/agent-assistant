namespace InvestorAssistant.Agents;

public interface IInvestorAgent
{
    string Category { get; }
    Task<string> HandleAsync(string investorId, string question, CancellationToken ct = default);
}
