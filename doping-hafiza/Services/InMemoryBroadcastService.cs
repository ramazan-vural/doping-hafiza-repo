using System.Collections.Concurrent;
using doping_hafiza.Models;

namespace doping_hafiza.Services;

/// <summary>
/// Prototip ve vaka çalışması için in-memory thread-safe yayın yönetim servisi.
/// Gerçekçi mock veri yönetimi, erteleme token invalidasyonu ve operasyonel metrik hesaplamalarını içerir.
/// </summary>
public class InMemoryBroadcastService : IBroadcastService
{
    private readonly ConcurrentDictionary<Guid, BroadcastSession> _sessions = new();
    private readonly ILogger<InMemoryBroadcastService> _logger;

    // Sistem genelinde takip edilen 10 resmi kategori
    private static readonly string[] DefinedCategories =
    {
        "Yazılım & Teknoloji",
        "Veri Bilimi & Yapay Zeka",
        "Hukuk & KVKK",
        "Ürün Yönetimi & Girişimcilik",
        "Siber Güvenlik",
        "Dijital Pazarlama",
        "Bulut Bilişim & DevOps",
        "Mobil Uygulama Geliştirme",
        "Finans & Yatırım", // Bu hafta yayın planlanmamış kategori #1
        "UI/UX Tasarım"      // Bu hafta yayın planlanmamış kategori #2
    };

    public InMemoryBroadcastService(ILogger<InMemoryBroadcastService> logger)
    {
        _logger = logger;
        SeedInitialData();
    }

    public IReadOnlyList<string> GetAllDefinedCategories() => DefinedCategories;

    public Task<IReadOnlyList<BroadcastSession>> GetAllSessionsAsync()
    {
        var list = _sessions.Values
            .OrderBy(s => s.ScheduledAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<BroadcastSession>>(list);
    }

    public Task<BroadcastSession?> GetByIdAsync(Guid id)
    {
        _sessions.TryGetValue(id, out var session);
        return Task.FromResult(session);
    }

    public Task<BroadcastDashboardViewModel> GetDashboardViewModelAsync()
    {
        var allSessions = _sessions.Values.ToList();

        // Güncel haftanın sınırları (Pazartesi 00:00 - Pazar 23:59)
        var now = DateTime.Now;
        var diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        var weekStart = now.Date.AddDays(-1 * diff);
        var weekEnd = weekStart.AddDays(7).AddTicks(-1);

        // Bu haftaki yayınlar
        var thisWeekSessions = allSessions
            .Where(s => s.ScheduledAt >= weekStart && s.ScheduledAt <= weekEnd)
            .ToList();

        // Bu hafta yayın planlanan kategoriler
        var activeCategoriesThisWeek = thisWeekSessions
            .Select(s => s.Category)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Tanımlı 10 kategoriden bu hafta hiç yayın olmayanlar
        var missingCategories = DefinedCategories
            .Where(cat => !activeCategoriesThisWeek.Contains(cat))
            .ToList();

        var viewModel = new BroadcastDashboardViewModel
        {
            WeekStartDate = weekStart,
            WeekEndDate = weekEnd,
            TotalPlannedCount = allSessions.Count(s => s.Status == BroadcastStatus.Planned),
            TotalCompletedCount = allSessions.Count(s => s.Status == BroadcastStatus.Completed),
            TotalCancelledCount = allSessions.Count(s => s.Status == BroadcastStatus.Cancelled),
            OverdueCount = allSessions.Count(s => s.IsOverdue),
            Upcoming24HoursCount = allSessions.Count(s => s.IsUpcomingWithin24Hours),
            MissingCategories = missingCategories,
            AllCategories = DefinedCategories,
            Sessions = allSessions.OrderBy(s => s.ScheduledAt).ToList()
        };

        return Task.FromResult(viewModel);
    }

    public Task<bool> UpdateStatusAsync(Guid id, BroadcastStatus newStatus)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            _logger.LogWarning("Durum güncellenmek istenen yayın bulunamadı: {SessionId}", id);
            return Task.FromResult(false);
        }

        var oldStatus = session.Status;
        session.Status = newStatus;

        _logger.LogInformation(
            "Yayın durumu güncellendi. ID: {SessionId} | {OldStatus} -> {NewStatus} | Eğitmen: {Creator}",
            id, oldStatus, newStatus, session.CreatorName);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Yayını yeni bir tarihe erteler.
    /// 
    /// =========================================================================================
    /// TEKNİK MİMARİ NOTU: REMINDER INVALIDATION PATTERN (Optimistic Job Token Invalidation)
    /// =========================================================================================
    /// Dağıtık arka plan zamanlayıcılarında (Hangfire, Quartz.NET, AWS SQS Delay Queue veya RabbitMQ)
    /// önceden kuyruğa alınmış bir gecikmeli işi doğrudan kuyruktan iptal etmek zordur veya maliyetlidir.
    ///
    /// Bunun yerine sektör standardı 'Token Invalidation' kullanılır:
    /// 1. Her yayın için bir 'ReminderJobToken' (Guid) saklanır.
    /// 2. Yayın ertelendiğinde bu token Guid.NewGuid() ile yenilenir.
    /// 3. Arka plan işi tetiklendiğinde kuyruktaki token ile DB/bellekteki token karşılaştırılır.
    /// 4. Eğer token'lar uyuşmuyorsa, eski zamanlayıcının geçersiz kaldığı anlaşılır ve işlem no-op olarak yutulur.
    /// 5. Yeni tarih için yeni bir arka plan işi güncel token ile kuyruğa yazılır.
    /// =========================================================================================
    /// </summary>
    public Task<(bool Success, string Message, Guid NewToken)> RescheduleSessionAsync(Guid id, DateTime newScheduledAt, string? reason = null)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return Task.FromResult((false, "Yayın oturumu bulunamadı.", Guid.Empty));
        }

        if (newScheduledAt <= DateTime.Now)
        {
            return Task.FromResult((false, "Erteleme tarihi gelecekteki bir zaman olmalıdır.", Guid.Empty));
        }

        var oldDate = session.ScheduledAt;
        var oldToken = session.ReminderJobToken;

        // Reminder Invalidation: Yeni bir token atanarak eski arka plan tetikleyicisi geçersiz kılınır.
        var newToken = Guid.NewGuid();
        session.ReminderJobToken = newToken;
        session.ScheduledAt = newScheduledAt;
        session.Status = BroadcastStatus.Planned; // Ertelenen yayın tekrar Planned durumuna geçer

        _logger.LogInformation(
            "[REMINDER INVALIDATION] Yayın ID: {SessionId} ({Creator}) ertelendi. " +
            "Eski Tarih: {OldDate:dd.MM.yyyy HH:mm} -> Yeni Tarih: {NewDate:dd.MM.yyyy HH:mm}. " +
            "Eski Hatırlatıcı Token: {OldToken} iptal edildi -> Yeni Token: {NewToken} üretildi. " +
            "Gerekçe: {Reason}",
            id, session.CreatorName, oldDate, newScheduledAt, oldToken, newToken, reason ?? "Belirtilmedi");

        return Task.FromResult((true, $"Yayın {newScheduledAt:dd.MM.yyyy HH:mm} tarihine başarıyla ertelendi.", newToken));
    }

    /// <summary>
    /// Operasyon ekibinin eğitmenle hızlıca iletişime geçebilmesi için yayının o anki durumuna
    /// göre otomatik özelleştirilmiş WhatsApp / E-Posta mesaj şablonu üretir.
    /// </summary>
    public Task<string> GenerateMessageTemplateAsync(Guid id)
    {
        if (!_sessions.TryGetValue(id, out var session))
        {
            return Task.FromResult("Yayın bulunamadı.");
        }

        string message;
        var formattedDate = session.ScheduledAt.ToString("dd MMMM dddd, HH:mm");

        if (session.Status == BroadcastStatus.Completed)
        {
            message = $"Sayın {session.CreatorName},\n\n" +
                      $"\"{session.Topic}\" konulu canlı yayın başarıyla tamamlanmıştır. " +
                      $"Değerli bilgi ve katkılarınız için teşekkür ederiz. Yayın performans raporu operasyon panelinize eklenecektir.\n\n" +
                      $"İyi çalışmalar dileriz,\nCanlı Yayın Operasyon Ekibi";
        }
        else if (session.Status == BroadcastStatus.Cancelled)
        {
            message = $"Sayın {session.CreatorName},\n\n" +
                      $"{formattedDate} tarihinde planlanmış olan \"{session.Topic}\" başlıklı canlı yayınınız takvimde \"Gerçekleşmedi\" olarak işaretlenmiştir. " +
                      $"Yeni bir yayın tarihi planlamak veya bilgi almak için operasyon ekibimizle dilediğiniz zaman iletişime geçebilirsiniz.\n\n" +
                      $"Canlı Yayın Operasyon Ekibi";
        }
        else if (session.IsOverdue)
        {
            var delayMinutes = (int)(DateTime.Now - session.ScheduledAt).TotalMinutes;
            message = $"⚠️ ACİL / DURUM SORGULAMA\n\n" +
                      $"Sayın {session.CreatorName},\n\n" +
                      $"Bugün saat {session.ScheduledAt:HH:mm} için planlanan \"{session.Topic}\" başlıklı canlı yayınınız henüz başlatılmadı olarak görünmektedir (~{delayMinutes} dk gecikme).\n\n" +
                      $"Teknik bir problem veya erteleme ihtiyacı söz konusu ise operasyon ekibimiz desteğe hazırdır. Lütfen acil olarak güncel durumu bize iletebilir misiniz?\n\n" +
                      $"Canlı Yayın Operasyon Destek Hattı";
        }
        else if (session.IsUpcomingWithin24Hours)
        {
            message = $"⏰ YAYIN HATIRLATMASI (YAKLAŞAN YAYIN)\n\n" +
                      $"Sayın {session.CreatorName},\n\n" +
                      $"Canlı yayınınıza 24 saatten az bir süre kaldı!\n" +
                      $"📅 Tarih/Saat: {formattedDate}\n" +
                      $"🎯 Konu: {session.Topic}\n" +
                      $"📂 Kategori: {session.Category}\n\n" +
                      $"Yayın öncesi ses, kamera ve internet kontrollerinizi yapmanızı, yayın odası bağlantısını test etmenizi rica ederiz. Sorularınız için bize dilediğiniz zaman yazabilirsiniz.\n\n" +
                      $"Başarılı bir yayın dileriz,\nCanlı Yayın Operasyon Ekibi";
        }
        else
        {
            message = $"Sayın {session.CreatorName},\n\n" +
                      $"\"{session.Topic}\" başlıklı canlı yayınınız {formattedDate} tarihi için takvime alınmıştır.\n\n" +
                      $"Yayın gününden 24 saat önce teknik kontrol ve hatırlatma mesajı tarafınıza iletilecektir.\n\n" +
                      $"Canlı Yayın Operasyon Ekibi";
        }

        return Task.FromResult(message);
    }

    /// <summary>
    /// Case study isterlerine uygun 10 farklı içerik üreticisi ve güncel haftaya yayılmış mock veriler.
    /// - 2 Kategori kasten boş bırakılmıştır (Finans & Yatırım, UI/UX Tasarım)
    /// - Bazıları geçmişte kalmış 'Planned' (Gecikmiş)
    /// - Bazıları <24 saat içinde 'Planned' (Yaklaşan)
    /// - Bazıları 'Completed'
    /// - Bazıları 'Cancelled'
    /// </summary>
    private void SeedInitialData()
    {
        var now = DateTime.Now;

        var mockSessions = new List<BroadcastSession>
        {
            // 1. Gecikmiş Yayın #1 (3 saat önce planlanmış, hala Planned)
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CreatorName = "Dr. Alperen Çelik",
                Category = "Yazılım & Teknoloji",
                Topic = ".NET 9 & C# 13 ile Yüksek Performanslı Web API Mimarisi",
                ScheduledAt = now.AddHours(-3),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 2. Gecikmiş Yayın #2 (1.5 saat önce planlanmış, hala Planned)
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                CreatorName = "Av. Selin Yılmaz",
                Category = "Hukuk & KVKK",
                Topic = "Yapay Zeka Araçlarında Telif Hakları ve KVKK Riskleri",
                ScheduledAt = now.AddHours(-1.5),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 3. Yaklaşan Yayın #1 (4 saat sonra, <24s)
            new()
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                CreatorName = "Prof. Dr. Kerem Akgün",
                Category = "Veri Bilimi & Yapay Zeka",
                Topic = "Kurumsal RAG Sistemleri ve LLM Entegrasyon Teknikleri",
                ScheduledAt = now.AddHours(4),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 4. Yaklaşan Yayın #2 (10 saat sonra, <24s)
            new()
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CreatorName = "Ece Demir",
                Category = "Ürün Yönetimi & Girişimcilik",
                Topic = "B2B SaaS Ürünlerinde Ürün-Pazar Uyumu (PMF) ve Metrikler",
                ScheduledAt = now.AddHours(10),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 5. Yaklaşan Yayın #3 (21 saat sonra, <24s)
            new()
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                CreatorName = "Mert Karaca",
                Category = "Siber Güvenlik",
                Topic = "Zero Trust Mimarisi ve Bulut Ortamlarında Tehdit Avcılığı",
                ScheduledAt = now.AddHours(21),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 6. Gelecek Yayın (2 gün sonra)
            new()
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                CreatorName = "Zeynep Barış",
                Category = "Dijital Pazarlama",
                Topic = "Performans Pazarlamasında Dönüşüm Oranı Optimizasyonu (CRO)",
                ScheduledAt = now.AddDays(2).Date.AddHours(14),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 7. Gelecek Yayın (3 gün sonra)
            new()
            {
                Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                CreatorName = "Caner Öztürk",
                Category = "Bulut Bilişim & DevOps",
                Topic = "Kubernetes Üzerinde Sıfır Kesinti (Zero-Downtime) Dağıtım",
                ScheduledAt = now.AddDays(3).Date.AddHours(16),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 8. Gelecek Yayın (4 gün sonra)
            new()
            {
                Id = Guid.Parse("88888888-8888-8888-8888-888888888888"),
                CreatorName = "Ayşe Korkmaz",
                Category = "Mobil Uygulama Geliştirme",
                Topic = "Modern Android & iOS Geliştirmede Mimari Desenler",
                ScheduledAt = now.AddDays(4).Date.AddHours(19),
                Status = BroadcastStatus.Planned,
                ReminderJobToken = Guid.NewGuid()
            },

            // 9. Tamamlanmış Yayın (Dün gerçekleşti)
            new()
            {
                Id = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                CreatorName = "Burak Şen",
                Category = "Yazılım & Teknoloji",
                Topic = "PostgreSQL Query Tuning ve İndeksleme Stratejileri",
                ScheduledAt = now.AddDays(-1).Date.AddHours(20),
                Status = BroadcastStatus.Completed,
                ReminderJobToken = Guid.NewGuid()
            },

            // 10. Tamamlanmış Yayın (2 gün önce gerçekleşti)
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                CreatorName = "Deniz Arda",
                Category = "Veri Bilimi & Yapay Zeka",
                Topic = "Endüstride Zaman Serisi Tahminleme ve Anomali Tespiti",
                ScheduledAt = now.AddDays(-2).Date.AddHours(15),
                Status = BroadcastStatus.Completed,
                ReminderJobToken = Guid.NewGuid()
            },

            // 11. İptal Edilmiş Yayın (Eğitmen mazereti nedeniyle iptal)
            new()
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CreatorName = "Caner Öztürk",
                Category = "Bulut Bilişim & DevOps",
                Topic = "Serverless Mimarilerde Cold-Start Optimizasyonu",
                ScheduledAt = now.AddDays(-1).Date.AddHours(11),
                Status = BroadcastStatus.Cancelled,
                ReminderJobToken = Guid.NewGuid()
            }
        };

        foreach (var session in mockSessions)
        {
            _sessions[session.Id] = session;
        }
    }
}
