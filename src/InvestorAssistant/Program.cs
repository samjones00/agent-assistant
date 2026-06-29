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

var investorIdIdx = Array.IndexOf(args, "--investor-id");
var investorId = (investorIdIdx >= 0 && investorIdIdx + 1 < args.Length) ? args[investorIdIdx + 1]
    : args.FirstOrDefault(a => a.StartsWith("--investor-id="))?.Split('=', 2).LastOrDefault();

var llmConfig = LlmConfigurationResolver.Resolve(config, args);
var endpoint = llmConfig.Endpoint;
var modelId = llmConfig.ModelId;
var apiKey = llmConfig.ApiKey;
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

var systemPrompt = LoadEmbeddedResource("InvestorAssistant.Prompts.system.md")
    + "\n\n" + LoadEmbeddedResource("InvestorAssistant.Prompts.templates.md");

var tools = new List<AIFunction>
{
    AIFunctionFactory.Create(PortfolioHelperTool.GetPortfolioOverviewDirect, "Portfolio Overview"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetSinglePositionDirect, "Single Position"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetDistributionsDirect, "Distributions"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetObligationsDirect, "Obligations"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetFeesDirect, "Fees"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetValuationsDirect, "Valuations"),
    AIFunctionFactory.Create(PortfolioHelperTool.GetAccountStatementDirect, "Account Statement"),
    AIFunctionFactory.Create(CalcTool.Calculate, "Calculate"),
    AIFunctionFactory.Create(CalcTool.ConvertCurrency, "Convert Currency"),
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
