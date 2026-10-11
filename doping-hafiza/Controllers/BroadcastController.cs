using doping_hafiza.Models;
using doping_hafiza.Services;
using Microsoft.AspNetCore.Mvc;

namespace doping_hafiza.Controllers;

/// <summary>
/// Canlı yayın operasyon paneli controller'ı.
/// Tek sayfa (SPA benzeri pragmatik MVC) üzerinden durum güncellemeleri,
/// erteleme ve iletişim şablonu operasyonlarını yönetir.
/// </summary>
public class BroadcastController : Controller
{
    private readonly IBroadcastService _broadcastService;
    private readonly ILogger<BroadcastController> _logger;

    public BroadcastController(IBroadcastService broadcastService, ILogger<BroadcastController> logger)
    {
        _broadcastService = broadcastService;
        _logger = logger;
    }

    /// <summary>
    /// Operasyon paneli ana görünümü.
    /// Üst metrik kartları ve haftalık yayın tablosunu render eder.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = await _broadcastService.GetDashboardViewModelAsync();
        return View(model);
    }

    /// <summary>
    /// Yayın durumunu hızlı güncelleme (Completed / Cancelled / Planned).
    /// Hem AJAX çağrılarını hem de standart Form POST'u destekler.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus([FromForm] Guid id, [FromForm] BroadcastStatus status)
    {
        var updated = await _broadcastService.UpdateStatusAsync(id, status);

        if (!updated)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message = "Yayın bulunamadı veya güncellenemedi." });
            }
            TempData["ErrorMessage"] = "Yayın güncellenemedi.";
            return RedirectToAction(nameof(Index));
        }

        var statusText = status switch
        {
            BroadcastStatus.Completed => "Tamamlandı",
            BroadcastStatus.Cancelled => "Gerçekleşmedi",
            _ => "Planlandı"
        };

        var message = $"Yayın durumu '{statusText}' olarak başarıyla güncellendi.";

        if (IsAjaxRequest())
        {
            return Json(new { success = true, message });
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Yayını yeni bir tarih/saate erteleme endpoint'i.
    /// Eski arka plan zamanlayıcısını geçersiz kılmak için ReminderJobToken yenilenir.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule([FromForm] Guid id, [FromForm] DateTime newScheduledAt, [FromForm] string? reason)
    {
        var (success, message, newToken) = await _broadcastService.RescheduleSessionAsync(id, newScheduledAt, reason);

        if (!success)
        {
            if (IsAjaxRequest())
            {
                return Json(new { success = false, message });
            }
            TempData["ErrorMessage"] = message;
            return RedirectToAction(nameof(Index));
        }

        if (IsAjaxRequest())
        {
            return Json(new
            {
                success = true,
                message,
                newToken = newToken.ToString(),
                newScheduledAtFormatted = newScheduledAt.ToString("dd.MM.yyyy HH:mm")
            });
        }

        TempData["SuccessMessage"] = message;
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Eğitmen için duruma özel (gecikmiş, yaklaşan vb.) dinamik iletişim/hatırlatma mesajı üretir.
    /// Bootstrap Modal tarafından AJAX ile çağrılır.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMessage(Guid id)
    {
        var session = await _broadcastService.GetByIdAsync(id);
        if (session == null)
        {
            return Json(new { success = false, message = "Yayın bulunamadı." });
        }

        var template = await _broadcastService.GenerateMessageTemplateAsync(id);

        return Json(new
        {
            success = true,
            creatorName = session.CreatorName,
            topic = session.Topic,
            scheduledAt = session.ScheduledAt.ToString("dd MMMM dddd, HH:mm"),
            status = session.OperationalDisplayStatus,
            message = template
        });
    }

    private bool IsAjaxRequest()
    {
        return Request.Headers["X-Requested-With"] == "XMLHttpRequest"
               || Request.Headers.Accept.ToString().Contains("application/json");
    }
}
