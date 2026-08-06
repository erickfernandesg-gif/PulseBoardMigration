using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly WorkspaceService _workspaceService;

    public DashboardController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public async Task<IActionResult> Index()
    {
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        return View(await _workspaceService.GetDashboardAsync(userId));
    }
}
