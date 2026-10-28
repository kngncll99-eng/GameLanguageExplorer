namespace GameLanguageExplorer.Tests;

/// <summary>
/// WPF <see cref="System.Windows.Application"/> aynı AppDomain'de yalnızca bir kez
/// oluşturulabildiği için render testleri paralel çalıştırılmaz.
/// </summary>
[CollectionDefinition("WpfUi", DisableParallelization = true)]
public sealed class WpfUiCollection
{
}
