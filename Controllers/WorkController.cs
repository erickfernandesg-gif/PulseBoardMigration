using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class WorkController : Controller
{
    private readonly WorkManagementService _service;
    private readonly ILogger<WorkController> _logger;

    public WorkController(WorkManagementService service, ILogger<WorkController> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        return UserId(out var userId)
            ? View(await _service.GetMyWorkAsync(userId))
            : Unauthorized();
    }

    [HttpPost]
    public async Task<IActionResult> Handoff(
        Guid taskId,
        Guid toUserId,
        string stage,
        DateTime? handoffDueDate,
        int estimatedHours,
        int estimatedMinutes,
        string? notes,
        string? acceptanceCriteria,
        bool requiresAcceptance,
        Guid? acceptanceBy)
    {
        try
        {
            await _service.HandoffAsync(
                taskId, toUserId, stage, handoffDueDate,
                checked(Math.Max(0, estimatedHours) * 60 + Math.Clamp(estimatedMinutes, 0, 59)),
                notes, acceptanceCriteria, requiresAcceptance, acceptanceBy);
            return Json(new { success = true });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao transferir tarefa {TaskId}", taskId);
            return BadRequest(new { success = false, message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Respond(Guid assignmentId, string action, string? note)
    {
        try
        {
            await _service.RespondAssignmentAsync(assignmentId, action, note);
            TempData["Success"] = action switch
            {
                "accept" => "Atribuição aceita.",
                "complete" => "Etapa concluída.",
                _ => "Atribuição devolvida."
            };
        }
        catch (Exception exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ReturnWithQuestion(Guid taskId, Guid toUserId, string question)
    {
        try
        {
            await _service.ReturnWithQuestionAsync(taskId, toUserId, question);
            return Json(new { success = true });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao devolver tarefa {TaskId} com dúvida", taskId);
            return BadRequest(new { success = false, message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Review(Guid taskId, string action, string? note)
    {
        try
        {
            await _service.ReviewTaskAsync(taskId, action, note);
            TempData["Success"] = action == "approve" ? "Entrega aprovada e concluída." : "Ajustes solicitados.";
        }
        catch (Exception exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AddDependency(Guid taskId, Guid dependsOnTaskId)
    {
        try
        {
            await _service.AddDependencyAsync(taskId, dependsOnTaskId);
            return Json(new { success = true });
        }
        catch (Exception exception)
        {
            return BadRequest(new { success = false, message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteDependency(Guid dependencyId)
    {
        try
        {
            await _service.DeleteDependencyAsync(dependencyId);
            return Json(new { success = true });
        }
        catch (Exception exception)
        {
            return BadRequest(new { success = false, message = exception.Message });
        }
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
