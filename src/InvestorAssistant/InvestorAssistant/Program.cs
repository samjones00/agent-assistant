using System.Reflection;
using InvestorAssistant.Agents;
using InvestorAssistant.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using System.ClientModel;

var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var providerName = args.Length > 0 ? args[0] : config["LLM:Default"] ?? "GitHubModels";
var providerKey = $"LLM:Providers:{providerName}";
var endpoint = config[$"{providerKey}:Endpoint"]
    ?? throw new InvalidOperationException($"Missing Endpoint for LLM provider '{providerName}'");
var modelId = config[$"{providerKey}:ModelId"]
    ?? throw new InvalidOperationException($"Missing ModelId for LLM provider '{providerName}'");
var apiKey = config[$"{providerKey}:ApiKey"] ?? "";
var dataDirectory = config["DataDirectory"] ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "EquiTie - Senior Software Engineer - Case Study", "data"));

QueryCsvTool.Configure(dataDirectory);
CalcTool.LoadFxRates(dataDirectory);

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
    AIFunctionFactory.Create(QueryCsvTool.QueryCsvAsync, "query_csv"),
    AIFunctionFactory.Create(CalcTool.Calculate, "calculate"),
    AIFunctionFactory.Create(CalcTool.ConvertCurrency, "convert_currency"),
    AIFunctionFactory.Create(CategoryTool.ListCategories, "list_categories"),
};

var orchestrator = new OrchestratorAgent(chatClient, systemPrompt, tools, dataDirectory);
await orchestrator.RunAsync();

static string LoadEmbeddedResource(string resourceName)
{
    var assembly = Assembly.GetExecutingAssembly();
    using var stream = assembly.GetManifestResourceStream(resourceName)
        ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}
