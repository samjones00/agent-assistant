using System.Reflection;
using InvestorAssistant.Agents;
using InvestorAssistant.Configuration;
using InvestorAssistant.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using System.ClientModel;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var llmConfig = LlmConfigurationResolver.Resolve(config, args);
var endpoint = llmConfig.Endpoint;
var modelId = llmConfig.ModelId;
var apiKey = llmConfig.ApiKey;
var investorId = llmConfig.InvestorId;
var dataDirectory = config["DataDirectory"] ?? throw new InvalidOperationException("Missing DataDirectory in config");

QueryCsvTool.Configure(dataDirectory);
CalcTool.LoadFxRates(dataDirectory);
ColumnMappings.Load(dataDirectory);

var chatClient = new OpenAIClient(
        new ApiKeyCredential(string.IsNullOrEmpty(apiKey) ? "ignored" : apiKey),
        new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint),
            NetworkTimeout = TimeSpan.FromMinutes(5),
        })
    .GetChatClient(modelId)
    .AsIChatClient();

var systemPrompt = LoadEmbeddedResource("InvestorAssistant.Prompts.system.md");

var tools = new List<AIFunction>
{
    AIFunctionFactory.Create(PortfolioHelperTool.GetPortfolioOverviewDirect, "portfolio_overview"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetSinglePositionDirect, "single_position"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetDistributionsDirect, "distributions"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetObligationsDirect, "obligations"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetFeesDirect, "fees"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetValuationsDirect, "valuations"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetAccountStatementDirect, "account_statement"),
    AIFunctionFactory.Create(CalcTool.Calculate, "calculate"),
    AIFunctionFactory.Create(CalcTool.ConvertCurrency, "convert_currency"),
};

var orchestrator = new OrchestratorAgent(chatClient, systemPrompt, tools, dataDirectory);
await orchestrator.RunAsync(investorId);

static string LoadEmbeddedResource(string resourceName)
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
