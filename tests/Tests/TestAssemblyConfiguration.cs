using Xunit;

// Bu test paketi gerçek geçici dosyalar ve SQLite veritabanları kullanır.
// Paralel koleksiyonlar Windows dosya kilidi/antivirüs zamanlaması nedeniyle
// aralıklı çakışmalara yol açabildiği için dosya/DB testleri seri çalıştırılır.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
