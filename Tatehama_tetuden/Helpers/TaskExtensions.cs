using Microsoft.Extensions.Logging;

namespace Tatehama_tetuden.Helpers;

public static class TaskExtensions
{
    public static async void FireAndForget(this Task task, ILogger? logger = null, string? context = null)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Fire-and-forget 例外: {Context}", context ?? "unknown");
        }
    }
}
