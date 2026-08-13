using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class SettingsController : Controller
{
    private readonly WorkspaceService _workspaceService;
    private readonly WorkManagementService _workManagementService;

    public SettingsController(WorkspaceService workspaceService, WorkManagementService workManagementService)
    {
        _workspaceService = workspaceService;
        _workManagementService = workManagementService;
    }

    public async Task<IActionResult> Index()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return Challenge();
        }

        var profile = await _workspaceService.GetProfileAsync(id);
        if (profile == null) return NotFound();
        return View(new SettingsViewModel
        {
            Profile = profile,
            Notifications = await _workManagementService.GetNotificationPreferenceAsync(id)
        });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateProfile(string fullName)
    {
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
            !string.IsNullOrWhiteSpace(fullName))
        {
            await _workspaceService.UpdateProfileAsync(id, fullName);
            TempData["Success"] = "Perfil atualizado.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateNotifications(
        bool inApp, bool emailDigest, bool dueReminders, bool budgetAlerts, bool mentionAlerts, short digestHour)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Challenge();
        await _workManagementService.SaveNotificationPreferenceAsync(new NotificationPreference
        {
            UserId = id,
            InApp = inApp,
            EmailDigest = emailDigest,
            DueReminders = dueReminders,
            BudgetAlerts = budgetAlerts,
            MentionAlerts = mentionAlerts,
            DigestHour = digestHour
        });
        TempData["Success"] = "Preferências de alertas atualizadas.";
        return RedirectToAction(nameof(Index));
    }
}
