using doping_hafiza.Models;

namespace doping_hafiza.Services;

/// <summary>
/// Canlı yayın operasyon paneli servis sözleşmesi.
/// Pragmatik, in-memory veri yönetimi ve operasyonel aksiyonları sağlar.
/// </summary>
public interface IBroadcastService
{
    /// <summary>
    /// Dashboard üst metrikleri ve oturum listesini içeren ViewModel'i getirir.
    /// </summary>
    Task<BroadcastDashboardViewModel> GetDashboardViewModelAsync();

    /// <summary>
    /// Tüm yayın oturumlarını planlanan tarihe göre sıralı döner.
    /// </summary>
    Task<IReadOnlyList<BroadcastSession>> GetAllSessionsAsync();

    /// <summary>
    /// Tekil bir yayın oturumunu getirir.
    /// </summary>
    Task<BroadcastSession?> GetByIdAsync(Guid id);

    /// <summary>
    /// Yayın durumunu günceller (örn. Planned -> Completed / Cancelled).
    /// </summary>
    Task<bool> UpdateStatusAsync(Guid id, BroadcastStatus newStatus);

    /// <summary>
    /// Yayını yeni bir tarihe erteler.
    /// Eski hatırlatıcıyı geçersiz kılmak için ReminderJobToken'ı yeniler ve loglar.
    /// </summary>
    Task<(bool Success, string Message, Guid NewToken)> RescheduleSessionAsync(Guid id, DateTime newScheduledAt, string? reason = null);

    /// <summary>
    /// Yayının anlık durumuna göre (gecikmiş, yaklaşan, tamamlanan vb.) dinamik iletişim/hatırlatma metni üretir.
    /// </summary>
    Task<string> GenerateMessageTemplateAsync(Guid id);

    /// <summary>
    /// Sistemde tanımlı 10 kategorinin listesini döner.
    /// </summary>
    IReadOnlyList<string> GetAllDefinedCategories();
}
