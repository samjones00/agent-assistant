using FluentAssertions;
using InvestorAssistant.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace InvestorAssistant.Tests;

public static class LlmConfigurationResolverTests
{
    private static IConfiguration BuildConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LLM:Default"] = "GitHubModels",
            ["LLM:Providers:GitHubModels:Endpoint"] = "https://github",
            ["LLM:Providers:GitHubModels:ModelId"] = "gpt-4o-mini",
            ["LLM:Providers:GitHubModels:ApiKey"] = "gh",
            ["LLM:Providers:Ollama:Endpoint"] = "http://ollama",
            ["LLM:Providers:Ollama:ModelId"] = "llama3.1:latest"
        }!)
        .Build();

    [Fact]
    public static void Resolve_UsesDefaultProvider_WhenNoOverrides()
    {
        var config = BuildConfig();

        var result = LlmConfigurationResolver.Resolve(config, Array.Empty<string>());

        result.ProviderName.Should().Be("GitHubModels");
        result.ModelId.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public static void Resolve_UsesProviderFromPositionalArgument()
    {
        var config = BuildConfig();
        var args = new[] { "Ollama" };

        var result = LlmConfigurationResolver.Resolve(config, args);

        result.ProviderName.Should().Be("Ollama");
        result.ModelId.Should().Be("llama3.1:latest");
    }

    [Fact]
    public static void Resolve_Throws_WhenProviderMissing()
    {
        var config = BuildConfig();
        var args = new[] { "NonExisting" };

        var action = () => LlmConfigurationResolver.Resolve(config, args);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("LLM provider 'NonExisting' is not configured.*");
    }
}
