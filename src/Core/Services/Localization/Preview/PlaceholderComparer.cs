namespace GameLanguageExplorer.Core.Services.Localization.Preview;

/// <summary>Yer tutucu sırasının nasıl karşılaştırılacağını belirtir.</summary>
public enum PlaceholderOrderPolicy
{
    /// <summary>Tür ve değerlerin kaynak metindeki sırayla bulunmasını gerektirir.</summary>
    Strict = 0
}

/// <summary>Yer tutucu karşılaştırmasında bulunan farkın türü.</summary>
public enum PlaceholderComparisonIssueType
{
    MissingToken = 0,
    ExtraToken = 1,
    TokenKindChanged = 2,
    TokenFormatChanged = 3,
    TokenCountMismatch = 4,
    TokenOrderChanged = 5
}

/// <summary>
/// Tek bir yer tutucu farkı. Adet alanları eksik/fazla ve adet uyuşmazlığı sorunlarında doludur;
/// tür, biçim ve sıra değişimlerinde <c>null</c> olur.
/// </summary>
public sealed record PlaceholderComparisonIssue(
    PlaceholderComparisonIssueType Type,
    PlaceholderToken? SourceToken,
    PlaceholderToken? TranslationToken,
    int? SourceCount = null,
    int? TranslationCount = null);

/// <summary>Kaynak ve çeviri metinlerinin salt okunur yer tutucu karşılaştırması.</summary>
public sealed record PlaceholderComparisonResult(
    PlaceholderScanResult SourceScan,
    PlaceholderScanResult TranslationScan,
    PlaceholderOrderPolicy OrderPolicy,
    IReadOnlyList<PlaceholderComparisonIssue> Issues)
{
    /// <summary>Yalnızca yer tutucuların seçilen politikaya göre uyumlu olup olmadığını belirtir.</summary>
    public bool IsMatch => Issues.Count == 0;

    /// <summary>Kaynak metinde tarayıcının tanıdığı token bulunmadığını belirtir.</summary>
    public bool SourceHasNoTokens => !SourceScan.HasPlaceholders;

    /// <summary>Çeviri metninde tarayıcının tanıdığı token bulunmadığını belirtir.</summary>
    public bool TranslationHasNoTokens => !TranslationScan.HasPlaceholders;
}

/// <summary>
/// Kaynak ve çeviriyi ayrı ayrı <see cref="PlaceholderScanner"/> ile tarar ve tokenları karşılaştırır.
/// Metinleri, mevcut tarama sonuçlarını veya imza sözleşmelerini değiştirmez.
/// </summary>
public static class PlaceholderComparer
{
    /// <summary>
    /// Varsayılan strict-order karşılaştırması <see cref="PlaceholderScanResult.TypedSignature"/> ile
    /// aynı tür/değer/sıra sözleşmesini kullanır; metin içindeki karakter konumlarını karşılaştırmaz.
    /// Yeniden sıralama politikası yoktur: mevcut token modeli printf argüman indeksini ve bir tokenın
    /// güvenle yeniden sıralanıp sıralanamayacağını yapılandırılmış olarak taşımaz.
    /// Tanınmayan veya bozuk görünen metinler token olarak varsayılmaz.
    /// </summary>
    public static PlaceholderComparisonResult Compare(
        string? sourceText,
        string? translationText,
        PlaceholderOrderPolicy orderPolicy = PlaceholderOrderPolicy.Strict)
    {
        if (orderPolicy is not PlaceholderOrderPolicy.Strict)
        {
            throw new ArgumentOutOfRangeException(nameof(orderPolicy), orderPolicy, "Desteklenmeyen sıra politikası.");
        }

        var sourceScan = PlaceholderScanner.Scan(sourceText);
        var translationScan = PlaceholderScanner.Scan(translationText);
        var sourceStats = BuildStats(sourceScan.Tokens);
        var translationStats = BuildStats(translationScan.Tokens);

        // Önce türü ve değeri aynı tokenları adetleri kadar eşleştir. Böylece sıra farkı,
        // eksik/fazla token veya biçim değişimi olarak yanlış sınıflandırılmaz.
        var translationIndexes = new Dictionary<TokenIdentity, Queue<int>>();
        for (var i = 0; i < translationScan.Tokens.Count; i++)
        {
            var identity = IdentityOf(translationScan.Tokens[i]);
            if (!translationIndexes.TryGetValue(identity, out var indexes))
            {
                indexes = new Queue<int>();
                translationIndexes.Add(identity, indexes);
            }

            indexes.Enqueue(i);
        }

        var matchedTranslation = new bool[translationScan.Tokens.Count];
        var unmatchedSource = new List<int>();
        for (var i = 0; i < sourceScan.Tokens.Count; i++)
        {
            var identity = IdentityOf(sourceScan.Tokens[i]);
            if (translationIndexes.TryGetValue(identity, out var indexes) && indexes.Count > 0)
            {
                matchedTranslation[indexes.Dequeue()] = true;
            }
            else
            {
                unmatchedSource.Add(i);
            }
        }

        var unmatchedTranslation = new List<int>();
        for (var i = 0; i < matchedTranslation.Length; i++)
        {
            if (!matchedTranslation[i])
            {
                unmatchedTranslation.Add(i);
            }
        }

        // Yalnızca bir tarafta bulunan kimlikler olası tür/biçim değişimleridir. Karşı tarafta da
        // bulunan eşleşmemiş oluşumlar ise öncelikle adet farkıdır; gerektiğinde tek taraflı yeni
        // tokenla eşleşerek yinelenen bir tokenın biçim değişimini de ayırt edebilir.
        var sourceOnly = QueueWhere(unmatchedSource, sourceScan.Tokens,
            token => !translationStats.ContainsKey(IdentityOf(token)));
        var sourceExcess = QueueWhere(unmatchedSource, sourceScan.Tokens,
            token => translationStats.ContainsKey(IdentityOf(token)));
        var translationOnly = QueueWhere(unmatchedTranslation, translationScan.Tokens,
            token => !sourceStats.ContainsKey(IdentityOf(token)));
        var translationExcess = QueueWhere(unmatchedTranslation, translationScan.Tokens,
            token => sourceStats.ContainsKey(IdentityOf(token)));

        var issues = new List<PlaceholderComparisonIssue>();
        PairChanges(sourceOnly, translationOnly, sourceScan.Tokens, translationScan.Tokens, issues);
        PairChanges(sourceOnly, translationExcess, sourceScan.Tokens, translationScan.Tokens, issues);
        PairChanges(sourceExcess, translationOnly, sourceScan.Tokens, translationScan.Tokens, issues);
        // İki tarafta da bulunan tokenların fazlalıkları birbirine bağlanmaz; bunlar adet farkıdır.

        AddCountMismatches(sourceExcess, sourceScan.Tokens, translationScan.Tokens,
            sourceStats, translationStats, issues);
        AddCountMismatches(translationExcess, translationScan.Tokens, sourceScan.Tokens,
            translationStats, sourceStats, issues, reverseCounts: true);

        foreach (var group in GroupRemaining(sourceOnly, sourceScan.Tokens))
        {
            issues.Add(new PlaceholderComparisonIssue(
                PlaceholderComparisonIssueType.MissingToken,
                group.Token,
                null,
                group.Count,
                0));
        }

        foreach (var group in GroupRemaining(translationOnly, translationScan.Tokens))
        {
            issues.Add(new PlaceholderComparisonIssue(
                PlaceholderComparisonIssueType.ExtraToken,
                null,
                group.Token,
                0,
                group.Count));
        }

        // İçerik/adet farkı varken sıra ayrıca raporlanmaz. Hiç fark yoksa TypedSignature'ın
        // sıra sözleşmesi doğrudan uygulanır; böylece strict-order yeniden sıralamayı reddeder.
        if (issues.Count == 0 && sourceScan.TypedSignature != translationScan.TypedSignature)
        {
            issues.Add(CreateOrderIssue(sourceScan.Tokens, translationScan.Tokens));
        }

        return new PlaceholderComparisonResult(sourceScan, translationScan, orderPolicy, issues);
    }

    private static Dictionary<TokenIdentity, TokenStats> BuildStats(IReadOnlyList<PlaceholderToken> tokens)
    {
        var stats = new Dictionary<TokenIdentity, TokenStats>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var identity = IdentityOf(tokens[i]);
            if (stats.TryGetValue(identity, out var current))
            {
                current.Count++;
            }
            else
            {
                stats.Add(identity, new TokenStats(1, i));
            }
        }

        return stats;
    }

    private static Queue<int> QueueWhere(
        IEnumerable<int> indexes,
        IReadOnlyList<PlaceholderToken> tokens,
        Func<PlaceholderToken, bool> predicate)
        => new(indexes.Where(index => predicate(tokens[index])));

    private static void PairChanges(
        Queue<int> sourceIndexes,
        Queue<int> translationIndexes,
        IReadOnlyList<PlaceholderToken> sourceTokens,
        IReadOnlyList<PlaceholderToken> translationTokens,
        ICollection<PlaceholderComparisonIssue> issues)
    {
        while (sourceIndexes.Count > 0 && translationIndexes.Count > 0)
        {
            var sourceToken = sourceTokens[sourceIndexes.Dequeue()];
            var translationToken = translationTokens[translationIndexes.Dequeue()];
            var type = sourceToken.Kind == translationToken.Kind
                ? PlaceholderComparisonIssueType.TokenFormatChanged
                : PlaceholderComparisonIssueType.TokenKindChanged;

            issues.Add(new PlaceholderComparisonIssue(type, sourceToken, translationToken));
        }
    }

    private static void AddCountMismatches(
        Queue<int> remainingIndexes,
        IReadOnlyList<PlaceholderToken> tokens,
        IReadOnlyList<PlaceholderToken> otherTokens,
        IReadOnlyDictionary<TokenIdentity, TokenStats> stats,
        IReadOnlyDictionary<TokenIdentity, TokenStats> otherStats,
        ICollection<PlaceholderComparisonIssue> issues,
        bool reverseCounts = false)
    {
        var emitted = new HashSet<TokenIdentity>();
        foreach (var index in remainingIndexes)
        {
            var identity = IdentityOf(tokens[index]);
            if (!emitted.Add(identity))
            {
                continue;
            }

            var ownStats = stats[identity];
            var counterpartStats = otherStats[identity];
            var ownToken = tokens[ownStats.FirstIndex];
            var otherToken = otherTokens[counterpartStats.FirstIndex];
            issues.Add(new PlaceholderComparisonIssue(
                PlaceholderComparisonIssueType.TokenCountMismatch,
                reverseCounts ? otherToken : ownToken,
                reverseCounts ? ownToken : otherToken,
                reverseCounts ? counterpartStats.Count : ownStats.Count,
                reverseCounts ? ownStats.Count : counterpartStats.Count));
        }
    }

    private static List<TokenGroup> GroupRemaining(
        IEnumerable<int> indexes,
        IReadOnlyList<PlaceholderToken> tokens)
    {
        var groups = new List<TokenGroup>();
        var groupIndexes = new Dictionary<TokenIdentity, int>();
        foreach (var index in indexes)
        {
            var token = tokens[index];
            var identity = IdentityOf(token);
            if (groupIndexes.TryGetValue(identity, out var groupIndex))
            {
                groups[groupIndex].Count++;
            }
            else
            {
                groupIndexes.Add(identity, groups.Count);
                groups.Add(new TokenGroup(token));
            }
        }

        return groups;
    }

    private static PlaceholderComparisonIssue CreateOrderIssue(
        IReadOnlyList<PlaceholderToken> sourceTokens,
        IReadOnlyList<PlaceholderToken> translationTokens)
    {
        var count = Math.Min(sourceTokens.Count, translationTokens.Count);
        for (var i = 0; i < count; i++)
        {
            if (IdentityOf(sourceTokens[i]) != IdentityOf(translationTokens[i]))
            {
                return new PlaceholderComparisonIssue(
                    PlaceholderComparisonIssueType.TokenOrderChanged,
                    sourceTokens[i],
                    translationTokens[i]);
            }
        }

        return new PlaceholderComparisonIssue(
            PlaceholderComparisonIssueType.TokenOrderChanged,
            null,
            null);
    }

    private static TokenIdentity IdentityOf(PlaceholderToken token) => new(token.Kind, token.Value);

    private readonly record struct TokenIdentity(PlaceholderKind Kind, string Value);

    private sealed class TokenStats(int count, int firstIndex)
    {
        public int Count { get; set; } = count;
        public int FirstIndex { get; } = firstIndex;
    }

    private sealed class TokenGroup(PlaceholderToken token)
    {
        public PlaceholderToken Token { get; } = token;
        public int Count { get; set; } = 1;
    }
}
