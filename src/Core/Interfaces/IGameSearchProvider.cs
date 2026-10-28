namespace GameLanguageExplorer.Core.Interfaces;

using GameLanguageExplorer.Core.Entities;

/// <summary>
/// Anahtar gerektirmeyen, isimle oyun arama yeteneği olan katalog sağlayıcıları için sözleşme.
/// Tam katalog listesi (API anahtarı gerekebilen) <see cref="IGameCatalogProvider"/> ile ayrıdır.
/// </summary>
public interface IGameSearchProvider
{
    /// <summary>
    /// Verilen terimle mağazada arama yapar ve eşleşen oyunları döndürür.
    /// Dönen <see cref="Game.ExternalId"/> mağaza uygulama kimliğini taşır.
    /// </summary>
    /// <param name="term">Aranacak oyun adı.</param>
    /// <param name="maxResults">Döndürülecek en fazla sonuç sayısı.</param>
    /// <param name="enrichDetails">True ise her sonuç için ayrıntılar (geliştirici, yayıncı, açıklama) da çekilir.</param>
    Task<IReadOnlyList<Game>> SearchGamesAsync(
        string term,
        int maxResults = 10,
        bool enrichDetails = true,
        CancellationToken cancellationToken = default);
}
