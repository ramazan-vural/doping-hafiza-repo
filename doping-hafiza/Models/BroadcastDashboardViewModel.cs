namespace doping_hafiza.Models;

/// <summary>
/// Canlı yayın operasyon paneli için aggregate edilmiş görünüm modeli.
/// Sayfa üzerindeki üst metrikler ve yayın listesini taşır.
/// </summary>
public class BroadcastDashboardViewModel
{
    /// <summary>
    /// Bu hafta planlanmış toplam yayın sayısı (Planned durumundaki tüm yayınlar).
    /// </summary>
    public int TotalPlannedCount { get; set; }

    /// <summary>
    /// Bu hafta başarıyla tamamlanmış yayın sayısı.
    /// </summary>
    public int TotalCompletedCount { get; set; }

    /// <summary>
    /// Bu hafta iptal edilen yayın sayısı.
    /// </summary>
    public int TotalCancelledCount { get; set; }

    /// <summary>
    /// Planlanan saati geçtiği halde başlatılmamış / tamamlanmamış (aksiyon bekleyen) yayın sayısı.
    /// </summary>
    public int OverdueCount { get; set; }

    /// <summary>
    /// Önümüzdeki 24 saat içinde gerçekleşecek yayın sayısı.
    /// </summary>
    public int Upcoming24HoursCount { get; set; }

    /// <summary>
    /// Sistemde tanımlı 10 kategoriden bu hafta HİÇ yayın planlanmamış olanların listesi.
    /// </summary>
    public IReadOnlyList<string> MissingCategories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Sistemde tanımlı tüm kategoriler.
    /// </summary>
    public IReadOnlyList<string> AllCategories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Operasyon tablosunda listelenecek yayın oturumları.
    /// </summary>
    public IReadOnlyList<BroadcastSession> Sessions { get; set; } = Array.Empty<BroadcastSession>();

    /// <summary>
    /// Hafta başlangıç tarihi.
    /// </summary>
    public DateTime WeekStartDate { get; set; }

    /// <summary>
    /// Hafta bitiş tarihi.
    /// </summary>
    public DateTime WeekEndDate { get; set; }
}
