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

    public SettingsController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public async Task<IActionResult> Index()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return Challenge();
        }

        var profile = await _workspaceService.GetProfileAsync(id);
        return profile == null ? NotFound() : View(new SettingsViewModel { Profile = profile });
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
}
