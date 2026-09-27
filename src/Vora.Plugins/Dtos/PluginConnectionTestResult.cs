namespace Vora.Plugins.Dtos;

public class PluginConnectionTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    public static PluginConnectionTestResult Ok(string message) => new() { Success = true, Message = message };
    public static PluginConnectionTestResult Fail(string message) => new() { Success = false, Message = message };
}
