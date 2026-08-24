using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class AutomationsController : Controller
{
    private readonly WorkspaceService _workspaceService;
    private readonly BoardOperationsService _boardOperationsService;

    public AutomationsController(WorkspaceService workspaceService, BoardOperationsService boardOperationsService)
    {
        _workspaceService = workspaceService;
        _boardOperationsService = boardOperationsService;
    }

    public async Task<IActionResult> Index(Guid boardId)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        return View(await _workspaceService.GetAutomationEditorAsync(boardId));
    }

    [HttpPost]
    public async Task<IActionResult> Save(AutomationRule rule)
    {
        if (!rule.BoardId.HasValue || !await CanManageBoardAsync(rule.BoardId.Value)) return Forbid();
        try { await _workspaceService.SaveAutomationAsync(rule); TempData["Success"] = "Automação salva."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { boardId = rule.BoardId });
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(Guid id, bool active, Guid? boardId)
    {
        if (!boardId.HasValue || !await CanManageBoardAsync(boardId.Value)) return Forbid();
        await _workspaceService.ToggleAutomationAsync(id, active);
        return RedirectToAction(nameof(Index), new { boardId });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, Guid? boardId)
    {
        if (!boardId.HasValue || !await CanManageBoardAsync(boardId.Value)) return Forbid();
        await _workspaceService.DeleteAutomationAsync(id);
        return RedirectToAction(nameof(Index), new { boardId });
    }

    private async Task<bool> CanManageBoardAsync(Guid boardId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userId, out var id) && await _boardOperationsService.CanManageBoardAsync(
            boardId, id, User.IsInRole("admin") || User.IsInRole("manager"));
    }
}
