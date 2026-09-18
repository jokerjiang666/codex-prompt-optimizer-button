using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public static class OptimizerProviderFactory
{
    public static IOptimizerProvider Create(AppSettings settings, string apiKey)
    {
        return string.Equals(settings.Provider, "openai-compatible", StringComparison.OrdinalIgnoreCase)
            ? new OpenAiCompatibleProvider(settings, apiKey)
            : new CodexCliProvider(settings);
    }
}
