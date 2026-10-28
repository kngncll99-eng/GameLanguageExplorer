namespace GameLanguageExplorer.Core.Entities;

public class PluginRegistration
{
    public int Id { get; set; }
    public string PluginName { get; set; } = string.Empty;
    public string AssemblyPath { get; set; } = string.Empty;
    public string[] SupportedFormats { get; set; } = Array.Empty<string>();
    public string[] SupportedEngines { get; set; } = Array.Empty<string>();
    public bool SupportsRead { get; set; }
    public bool SupportsWrite { get; set; }
    public string? Dependencies { get; set; }
    public string Version { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}