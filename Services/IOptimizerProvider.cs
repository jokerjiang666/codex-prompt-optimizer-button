namespace CodexInputEnhancer.Services;

public interface IOptimizerProvider
{
    Task<string> OptimizeAsync(string text, CancellationToken cancellationToken);
}
