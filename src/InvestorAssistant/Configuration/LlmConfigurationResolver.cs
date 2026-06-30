using Microsoft.Extensions.Configuration;

namespace InvestorAssistant.Configuration;

internal record LlmConfiguration(string ProviderName, string ModelId, string Endpoint, string ApiKey, string? InvestorId);

internal static class LlmConfigurationResolver
{
    public static LlmConfiguration Resolve(IConfiguration config, string[] args)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));
        if (args == null) throw new ArgumentNullException(nameof(args));

        var consumedIndices = new HashSet<int>();

        var investorId = GetOptionValue(args, "--investor-id", consumedIndices);

        var positionalProvider = args
            .Where((arg, index) => !consumedIndices.Contains(index) && !IsNamedOption(arg))
            .FirstOrDefault();

        var providerName = positionalProvider
            ?? config["LLM:Default"];

        if (string.IsNullOrWhiteSpace(providerName))
            throw new InvalidOperationException(
                "No LLM provider specified. Pass a provider name as argument or set LLM:Default in configuration.");

        var providerKey = $"LLM:Providers:{providerName}";
        var endpoint = config[$"{providerKey}:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException($"LLM provider '{providerName}' is not configured. Add it under LLM:Providers in configuration.");

        var modelId = config[$"{providerKey}:ModelId"];
        if (string.IsNullOrWhiteSpace(modelId))
            throw new InvalidOperationException($"Missing ModelId for LLM provider '{providerName}'.");

        var apiKey = config[$"{providerKey}:ApiKey"] ?? string.Empty;

        return new LlmConfiguration(providerName, modelId!, endpoint!, apiKey, investorId);
    }

    private static string? GetOptionValue(string[] args, string optionName, HashSet<int> consumed)
    {
        var equalsPrefix = optionName + "=";

        for (int i = 0; i < args.Length; i++)
        {
            if (consumed.Contains(i))
                continue;

            var current = args[i];
            if (current.Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                consumed.Add(i);
                if (i + 1 < args.Length)
                {
                    consumed.Add(i + 1);
                    return args[i + 1];
                }
                return null;
            }

            if (current.StartsWith(equalsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                consumed.Add(i);
                return current.Substring(equalsPrefix.Length);
            }
        }

        return null;
    }

    private static bool IsNamedOption(string arg) => arg.StartsWith("--", StringComparison.Ordinal);
}
