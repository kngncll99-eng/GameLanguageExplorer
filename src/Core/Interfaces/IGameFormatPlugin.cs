namespace GameLanguageExplorer.Core.Interfaces;

public interface IGameFormatPlugin
{
    string PluginName { get; }
    string Version { get; }
    string[] SupportedFormats { get; }
    string[] SupportedEngines { get; }
    bool SupportsRead { get; }
    bool SupportsWrite { get; }
    string[] Dependencies { get; }
    bool IsEnabled { get; set; }
}