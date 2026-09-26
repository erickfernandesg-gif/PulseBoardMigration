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
    private readonly AuthService _authService;

    public SettingsController(
        WorkspaceService workspaceService,
        WorkManagementService workManagementService,
        AuthService authService)
    {
        _workspaceService = workspaceService;
        _workManagementService = workManagementService;
        _authService = authService;
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
            try
            {
                if (await _workspaceService.UpdateProfileAsync(id, fullName)) TempData["Success"] = "Perfil atualizado.";
                else TempData["Error"] = "Não foi possível atualizar o perfil.";
            }
            catch (Exception exception) { TempData["Error"] = exception.Message; }
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateNotifications(
        bool inApp, bool emailDigest, bool dueReminders, bool budgetAlerts, bool mentionAlerts, short digestHour)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Challenge();
        try
        {
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
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (newPassword?.Length < 8)
        {
            TempData["Error"] = "A nova senha precisa ter pelo menos 8 caracteres.";
        }
        else if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            TempData["Error"] = "A confirmação não corresponde à nova senha.";
        }
        else
        {
            try
            {
                var email = User.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrWhiteSpace(email)) return Challenge();
                await _authService.ChangePasswordAsync(email, currentPassword, newPassword!);
                TempData["Success"] = "Senha alterada. Use a nova senha no próximo acesso.";
            }
            catch (Exception exception)
            {
                TempData["Error"] = exception.Message;
            }
        }

        return RedirectToAction(nameof(Index));
    }
}
