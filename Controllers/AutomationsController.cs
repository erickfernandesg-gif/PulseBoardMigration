using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class AutomationsController : Controller
{
    private readonly WorkspaceService _workspaceService;

    public AutomationsController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public async Task<IActionResult> Index(Guid? boardId)
    {
        ViewBag.BoardId = boardId;
        return View(await _workspaceService.GetAutomationsAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Save(AutomationRule rule)
    {
        await _workspaceService.SaveAutomationAsync(rule);
        TempData["Success"] = "Automação salva.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(Guid id, bool active)
    {
        await _workspaceService.ToggleAutomationAsync(id, active);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _workspaceService.DeleteAutomationAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
