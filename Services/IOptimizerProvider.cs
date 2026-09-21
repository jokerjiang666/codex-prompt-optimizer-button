namespace CodexInputEnhancer.Services;

public interface IOptimizerProvider
{
    Task<string> OptimizeAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
