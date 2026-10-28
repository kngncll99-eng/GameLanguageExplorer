namespace GameLanguageExplorer.Core.Services.Translation;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Geri yükleme öncesinde bulunan koruma belirteci sorununun türü.</summary>
public enum PlaceholderRestorationIssueType
{
    MissingSentinel = 0,
    ExtraSentinel = 1,
    ModifiedSentinel = 2,
    DuplicateSentinel = 3,

    /// <summary>Belirteçler sağlam olsa bile geri yüklenen metin kaynak yer tutucularıyla uyuşmuyor.</summary>
    PlaceholderMismatch = 4
}

/// <summary>Tek bir koruma belirteci sorunu. Ham çeviri metnini kopyalamaz.</summary>
public sealed record PlaceholderRestorationIssue(
    PlaceholderRestorationIssueType Type,
    string? Sentinel,
    int? ExpectedCount = null,
    int? ActualCount = null,
    string? Detail = null);

/// <summary>Kaynak metindeki bir yer tutucunun hangi benzersiz belirteçle maskelendiği.</summary>
public sealed record ProtectedPlaceholder(int Index, string Sentinel, PlaceholderToken Token);

/// <summary>Maskelenmiş kaynak ve geri yükleme için gerekli eşleme.</summary>
public sealed record PlaceholderProtectionResult(
    string SourceText,
    string MaskedText,
    string SentinelPrefix,
    IReadOnlyList<ProtectedPlaceholder> Placeholders,
    PlaceholderScanResult SourceScan)
{
    public bool HasPlaceholders => Placeholders.Count > 0;
}

/// <summary>
/// Doğrulanmış geri yükleme sonucu. <see cref="Text"/> yalnızca hiçbir sorun yoksa doludur;
/// doğrulanamayan yanıt sessizce düzeltilmez veya kabul edilmez.
/// </summary>
public sealed record PlaceholderRestorationResult(
    string? Text,
    IReadOnlyList<PlaceholderRestorationIssue> Issues,
    PlaceholderProtectionResult Protection,
    PlaceholderComparisonResult? Comparison = null)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Yer tutucuları çakışmayan sentinel'lerle maskeler ve model yanıtındaki sentinelleri
/// yalnızca eksiksiz/tekrarsız doğrulandığında geri yükler. Mevcut
/// <see cref="PlaceholderScanner"/> ve <see cref="PlaceholderComparer"/> sözleşmelerini kullanır.
/// </summary>
public static class PlaceholderProtector
{
    private const string SentinelMarker = "__GLE_PH";

    /// <summary>
    /// Kaynak metindeki tanınan tüm yer tutucuları, her oluşum için benzersiz bir sentinel ile
    /// değiştirir. Sentinel öneki kaynak metinde bulunmayacak şekilde seçilir; böylece kaynakta
    /// sentinel benzeri metin bulunması geri yüklemeyi bozmaz. Toplu çeviride aynı kaynak metnin
    /// farklı birimlerde aynı sentineli üretmemesi için kararlı <paramref name="scope"/> (ör.
    /// <c>unitId</c>) verilmelidir.
    /// </summary>
    public static PlaceholderProtectionResult Protect(string? sourceText, string? scope = null)
    {
        var text = sourceText ?? string.Empty;
        var scan = PlaceholderScanner.Scan(text);
        if (!scan.HasPlaceholders)
        {
            return new PlaceholderProtectionResult(
                text,
                text,
                string.Empty,
                Array.Empty<ProtectedPlaceholder>(),
                scan);
        }

        var prefix = SelectPrefix(text);
        var nonce = ComputeNonce(text, scope);
        var placeholders = new ProtectedPlaceholder[scan.Tokens.Count];
        for (var i = 0; i < scan.Tokens.Count; i++)
        {
            var sentinel = prefix
                + nonce
                + "_"
                + i.ToString("D4", CultureInfo.InvariantCulture)
                + "__";
            placeholders[i] = new ProtectedPlaceholder(i, sentinel, scan.Tokens[i]);
        }

        // Sondan başa değiştirmek, önceki token konumlarını geçersiz kılmaz.
        var masked = new StringBuilder(text);
        for (var i = placeholders.Length - 1; i >= 0; i--)
        {
            var placeholder = placeholders[i];
            masked.Remove(placeholder.Token.Start, placeholder.Token.Length);
            masked.Insert(placeholder.Token.Start, placeholder.Sentinel);
        }

        return new PlaceholderProtectionResult(
            text,
            masked.ToString(),
            prefix,
            placeholders,
            scan);
    }

    /// <summary>
    /// Model yanıtındaki sentinelleri doğrular ve yalnızca tüm beklenen sentineller tam olarak
    /// bir kez bulunduğunda geri yükler. Eksik, fazla, değiştirilmiş veya çoğaltılmış sentinel
    /// ile yer tutucu sırası değişikliği reddedilir.
    /// </summary>
    public static PlaceholderRestorationResult Restore(
        string? translationText,
        PlaceholderProtectionResult protection)
    {
        ArgumentNullException.ThrowIfNull(protection);

        var issues = new List<PlaceholderRestorationIssue>();
        var restoredText = translationText;

        if (protection.HasPlaceholders)
        {
            var occurrences = FindSentinelOccurrences(restoredText, protection.SentinelPrefix);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var occurrence in occurrences)
            {
                counts[occurrence.Candidate] = counts.GetValueOrDefault(occurrence.Candidate) + 1;
            }

            foreach (var placeholder in protection.Placeholders)
            {
                var count = counts.GetValueOrDefault(placeholder.Sentinel);
                if (count == 0)
                {
                    issues.Add(new PlaceholderRestorationIssue(
                        PlaceholderRestorationIssueType.MissingSentinel,
                        placeholder.Sentinel,
                        1,
                        0,
                        "Beklenen koruma belirteci çeviride bulunamadı."));
                }
                else if (count > 1)
                {
                    issues.Add(new PlaceholderRestorationIssue(
                        PlaceholderRestorationIssueType.DuplicateSentinel,
                        placeholder.Sentinel,
                        1,
                        count,
                        "Koruma belirteci çeviride birden fazla kez kullanılmış."));
                }
            }

            var expected = protection.Placeholders
                .Select(p => p.Sentinel)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var group in occurrences
                .Where(o => !expected.Contains(o.Candidate))
                .GroupBy(o => o.Candidate, StringComparer.Ordinal))
            {
                var occurrence = group.First();
                var type = IsWellFormedSentinel(occurrence, protection.SentinelPrefix)
                    ? PlaceholderRestorationIssueType.ExtraSentinel
                    : PlaceholderRestorationIssueType.ModifiedSentinel;
                issues.Add(new PlaceholderRestorationIssue(
                    type,
                    occurrence.Candidate,
                    0,
                    group.Count(),
                    type == PlaceholderRestorationIssueType.ExtraSentinel
                        ? "Çeviride kaynakta olmayan bir koruma belirteci var."
                        : "Koruma belirteci değiştirilmiş veya bozulmuş."));
            }

            if (issues.Count == 0 && restoredText is not null)
            {
                foreach (var placeholder in protection.Placeholders)
                {
                    restoredText = restoredText.Replace(
                        placeholder.Sentinel,
                        placeholder.Token.Value,
                        StringComparison.Ordinal);
                }
            }
        }

        var comparison = PlaceholderComparer.Compare(protection.SourceText, restoredText);
        if (issues.Count == 0 && !comparison.IsMatch)
        {
            issues.Add(new PlaceholderRestorationIssue(
                PlaceholderRestorationIssueType.PlaceholderMismatch,
                null,
                Detail: "Geri yüklenen metin kaynak yer tutucularıyla eşleşmiyor: "
                    + string.Join(", ", comparison.Issues.Select(i => i.Type))));
        }

        return new PlaceholderRestorationResult(
            issues.Count == 0 ? restoredText : null,
            issues,
            protection,
            comparison);
    }

    private static string SelectPrefix(string sourceText)
    {
        for (var i = 0; i < 1000; i++)
        {
            var prefix = i == 0 ? SentinelMarker + "_" : SentinelMarker + i + "_";
            if (!sourceText.Contains(prefix, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        throw new InvalidOperationException(
            "Kaynak metinle çakışmayan bir yer tutucu koruma öneki bulunamadı.");
    }

    private static string ComputeNonce(string sourceText, string? scope)
    {
        var seed = string.IsNullOrEmpty(scope)
            ? sourceText
            : scope + "\0" + sourceText;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private static IReadOnlyList<SentinelOccurrence> FindSentinelOccurrences(
        string? text,
        string prefix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(prefix))
        {
            return Array.Empty<SentinelOccurrence>();
        }

        var occurrences = new List<SentinelOccurrence>();
        var searchIndex = 0;
        while (searchIndex < text.Length)
        {
            var start = text.IndexOf(prefix, searchIndex, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var suffixStart = text.IndexOf(
                "__",
                start + prefix.Length,
                StringComparison.Ordinal);
            if (suffixStart < 0)
            {
                occurrences.Add(new SentinelOccurrence(text[start..], false));
                break;
            }

            var end = suffixStart + 2;
            occurrences.Add(new SentinelOccurrence(text[start..end], true));
            searchIndex = end;
        }

        return occurrences;
    }

    private static bool IsWellFormedSentinel(SentinelOccurrence occurrence, string prefix)
    {
        if (!occurrence.Terminated
            || !occurrence.Candidate.StartsWith(prefix, StringComparison.Ordinal)
            || !occurrence.Candidate.EndsWith("__", StringComparison.Ordinal))
        {
            return false;
        }

        var innerLength = occurrence.Candidate.Length - prefix.Length - 2;
        if (innerLength != 8 + 1 + 4)
        {
            return false;
        }

        var inner = occurrence.Candidate.Substring(prefix.Length, innerLength);
        return inner[8] == '_'
            && inner[..8].All(Uri.IsHexDigit)
            && inner[9..].All(char.IsAsciiDigit);
    }

    private readonly record struct SentinelOccurrence(string Candidate, bool Terminated);
}
