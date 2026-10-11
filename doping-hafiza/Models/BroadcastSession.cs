namespace doping_hafiza.Models;

/// <summary>
/// Canlı yayın oturumu domain modeli.
/// Operasyon paneli üzerinde planlama, izleme ve hatırlatıcı durumlarını barındırır.
/// </summary>
public class BroadcastSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Yayını gerçekleştirecek içerik üreticisinin / eğitmenin adı.
    /// </summary>
    public string CreatorName { get; set; } = string.Empty;

    /// <summary>
    /// Yayının ait olduğu kategori (örn. Yazılım & Teknoloji, Veri Bilimi vb.).
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Canlı yayın başlığı / konusu.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Yayının planlandığı tarih ve saat.
    /// </summary>
    public DateTime ScheduledAt { get; set; }

    /// <summary>
    /// Yayının mevcut durumu (Planned, Completed, Cancelled).
    /// </summary>
    public BroadcastStatus Status { get; set; } = BroadcastStatus.Planned;

    /// <summary>
    /// Arka plan bildirim kuyruğu (Hangfire/Quartz vb.) optimistik concurrency kontrolü için token.
    /// Yayın ertelendiğinde bu token yenilenir ve eski işlerin tetiklenmesi engellenir.
    /// </summary>
    public Guid ReminderJobToken { get; set; } = Guid.NewGuid();

    // =========================================================================
    // Domain / Operational Helper Properties
    // =========================================================================

    /// <summary>
    /// Yayın planlanmış durumda fakat zamanı geçmişse operasyonel aksiyon gerekir.
    /// </summary>
    public bool IsOverdue => Status == BroadcastStatus.Planned && ScheduledAt < DateTime.Now;

    /// <summary>
    /// Yayın önümüzdeki 24 saat içinde gerçekleşecekse yakın takip gerektirir.
    /// </summary>
    public bool IsUpcomingWithin24Hours => Status == BroadcastStatus.Planned 
                                          && ScheduledAt >= DateTime.Now 
                                          && ScheduledAt <= DateTime.Now.AddHours(24);

    /// <summary>
    /// Arayüzde kullanılacak insan tarafından okunabilir durum etiketi.
    /// </summary>
    public string OperationalDisplayStatus
    {
        get
        {
            if (Status == BroadcastStatus.Completed) return "Tamamlandı";
            if (Status == BroadcastStatus.Cancelled) return "Gerçekleşmedi";
            if (IsOverdue) return "Gecikmiş / Aksiyon Gerekli";
            if (IsUpcomingWithin24Hours) return "Yaklaşan Yayın (<24s)";
            return "Planlandı";
        }
    }

    /// <summary>
    /// Arayüz badge renk sınıfı.
    /// </summary>
    public string OperationalBadgeCssClass
    {
        get
        {
            if (Status == BroadcastStatus.Completed) return "bg-success";
            if (Status == BroadcastStatus.Cancelled) return "bg-secondary";
            if (IsOverdue) return "bg-danger";
            if (IsUpcomingWithin24Hours) return "bg-warning text-dark";
            return "bg-info text-dark";
        }
    }
}
