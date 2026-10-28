namespace GameLanguageExplorer.Core.Entities;

public class FileFormat
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty; // XML, JSON, CSV, TXT, INI, YAML, PO
    public string[] Extensions { get; set; } = Array.Empty<string>();
    public bool SupportsRead { get; set; }
    public bool SupportsWrite { get; set; }
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; } = true;
}