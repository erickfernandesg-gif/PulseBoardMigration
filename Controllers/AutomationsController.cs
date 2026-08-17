using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulseBoardMigration.Security.PulsePolicies.ManagerOrAdmin)]
public class AutomationsController : Controller
{
    private readonly WorkspaceService _workspaceService;

    public AutomationsController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public async Task<IActionResult> Index(Guid? boardId)
    {
        return View(await _workspaceService.GetAutomationEditorAsync(boardId));
    }

    [HttpPost]
    public async Task<IActionResult> Save(AutomationRule rule)
    {
        try { await _workspaceService.SaveAutomationAsync(rule); TempData["Success"] = "Automação salva."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { boardId = rule.BoardId });
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(Guid id, bool active, Guid? boardId)
    {
        await _workspaceService.ToggleAutomationAsync(id, active);
        return RedirectToAction(nameof(Index), new { boardId });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, Guid? boardId)
    {
        await _workspaceService.DeleteAutomationAsync(id);
        return RedirectToAction(nameof(Index), new { boardId });
    }
}
