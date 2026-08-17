using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Security;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class BoardOperationsController : Controller
{
    private readonly BoardOperationsService _service;
    public BoardOperationsController(BoardOperationsService service) => _service = service;

    public async Task<IActionResult> Index(Guid id)
    {
        if (!await CanManageBoardAsync(id)) return Forbid();
        return await _service.GetAsync(id) is { } model ? View(model) : NotFound();
    }

    [HttpPost]
    public async Task<IActionResult> SaveColumns(Guid boardId, List<string> columnId, List<string> title,
        List<string> color, List<int?> wipLimit, List<int> requiresApproval)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try
        {
            var approvalFlags = Enumerable.Range(0, columnId.Count).Select(i => requiresApproval.Contains(i)).ToList();
            await _service.SaveColumnsAsync(boardId, columnId, title, color, wipLimit, approvalFlags);
            TempData["Success"] = "Colunas, cores e limites de trabalho atualizados.";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> Bulk(Guid boardId, Guid[] taskIds, string action, Guid? assignedTo,
        string? status, DateTime? dueDate, string? priority)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.BulkActionAsync(boardId, taskIds, action, assignedTo, status, dueDate, priority); return Json(new { success = true }); }
        catch (Exception ex) { return BadRequest(new { success = false, message = ex.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> CreateIntake(Guid boardId, string title, string? description,
        string targetStatus, string priority, bool requireEmail)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            var token = await _service.CreateIntakeFormAsync(boardId, title, description, targetStatus, priority, requireEmail, userId);
            TempData["Success"] = $"Formulário criado: {Url.Action("Form", "Intake", new { token }, Request.Scheme)}";
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleIntake(Guid boardId, Guid id, bool active)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.SetIntakeActiveAsync(id, active); TempData["Success"] = active ? "Formulário ativado." : "Formulário pausado."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> AddApproval(Guid boardId, Guid taskId, int sequence, Guid approverId)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.AddApprovalStepAsync(taskId, sequence, approverId); TempData["Success"] = "Etapa de aprovação adicionada."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> DecideApproval(Guid boardId, Guid stepId, string decision, string? note)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.DecideApprovalAsync(stepId, decision, note); TempData["Success"] = "Decisão registrada."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteApproval(Guid boardId, Guid id)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.DeleteApprovalStepAsync(id); TempData["Success"] = "Etapa de aprovação removida."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> AddDelegation(Guid boardId, Guid delegatorId, Guid substituteId, DateTime startsOn, DateTime endsOn)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        if (!UserId(out var userId)) return Unauthorized();
        try { await _service.AddDelegationAsync(delegatorId, substituteId, startsOn, endsOn, userId); TempData["Success"] = "Substituição programada."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDelegation(Guid boardId, Guid id)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.DeleteDelegationAsync(id); TempData["Success"] = "Substituição removida."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> AddMirror(Guid boardId, Guid sourceTaskId, Guid targetTaskId, string fieldName)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        if (!UserId(out var userId)) return Unauthorized();
        try { await _service.AddMirrorAsync(sourceTaskId, targetTaskId, fieldName, userId); TempData["Success"] = "Espelhamento ativado."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteMirror(Guid boardId, Guid id)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.DeleteMirrorAsync(id); TempData["Success"] = "Espelhamento removido."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> AddCrossDependency(Guid boardId, Guid taskId, Guid dependsOnTaskId)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.AddCrossProjectDependencyAsync(taskId, dependsOnTaskId); TempData["Success"] = "Dependência entre projetos criada."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCrossDependency(Guid boardId, Guid id)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try { await _service.DeleteCrossProjectDependencyAsync(id); TempData["Success"] = "Dependência removida."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { id = boardId });
    }

    [HttpGet]
    public async Task<IActionResult> Import(Guid id) =>
        await CanManageBoardAsync(id) ? View(new BoardImportPreviewViewModel { BoardId = id }) : Forbid();

    [HttpPost]
    public async Task<IActionResult> PreviewImport(Guid boardId, IFormFile? file, string source)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        try
        {
            if (file == null) throw new InvalidOperationException("Selecione um arquivo para importar.");
            if (file.Length <= 0 || file.Length > 10 * 1024 * 1024) throw new InvalidOperationException("O arquivo deve ter até 10 MB.");
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not ".xlsx" and not ".csv") throw new InvalidOperationException("Envie um arquivo .xlsx ou .csv.");
            return View("Import", _service.ParseImport(boardId, file.OpenReadStream(), file.FileName, source));
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Import), new { id = boardId }); }
    }

    [HttpPost]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> CommitImport(Guid boardId, string payload, string titleColumn,
        string? descriptionColumn, string? statusColumn, string? priorityColumn, string? dueDateColumn, string? source)
    {
        if (!await CanManageBoardAsync(boardId)) return Forbid();
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            var count = await _service.CommitImportAsync(boardId, payload, titleColumn, descriptionColumn, statusColumn, priorityColumn, dueDateColumn, source, userId);
            TempData["Success"] = $"{count} tarefas importadas com sucesso.";
            return RedirectToAction("Details", "Boards", new { id = boardId });
        }
        catch (Exception ex) { TempData["Error"] = ex.Message; return RedirectToAction(nameof(Import), new { id = boardId }); }
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
    private async Task<bool> CanManageBoardAsync(Guid boardId) => UserId(out var userId) &&
        await _service.CanManageBoardAsync(boardId, userId, User.IsInRole("admin") || User.IsInRole("manager"));
}
