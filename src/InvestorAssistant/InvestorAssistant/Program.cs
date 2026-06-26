using InvestorAssistant.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using Azure.AI.OpenAI;
using System.ClientModel;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var provider = config["LLM:Provider"] ?? "Ollama";
var endpoint = config["LLM:Endpoint"]!;
var modelId = config["LLM:ModelId"]!;
var apiKey = config["LLM:ApiKey"] ?? "";

IChatClient chatClient = provider switch
{
    "Azure" => new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey))
        .GetChatClient(modelId)
        .AsIChatClient(),

    _ => new OpenAIClient(
            new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "ignored" : apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(modelId)
        .AsIChatClient(),
};

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

await orchestrator.RunAsync();
