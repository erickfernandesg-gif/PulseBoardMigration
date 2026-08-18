using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Security;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulsePolicies.ManagerOrAdmin)]
public class PlanningController : Controller
{
    private readonly EnterpriseService _service;
    public PlanningController(EnterpriseService service) => _service = service;

    public async Task<IActionResult> Index(Guid? teamId, Guid? boardId, DateTime? from, DateTime? to) =>
        View(await _service.GetPlanningAsync(UserId(), User.IsInRole("admin"), teamId, boardId, from, to));

    [HttpPost] public async Task<IActionResult> AddHoliday(DateTime date, string name, Guid? teamId) =>
        await Execute(() => _service.AddHolidayAsync(date, name, teamId, UserId()), "Feriado adicionado.");

    [HttpPost] public async Task<IActionResult> AddAbsence(Guid userId, string type, DateTime startsOn, DateTime endsOn, string? notes) =>
        await Execute(() => _service.AddAbsenceAsync(userId, type, startsOn, endsOn, notes, UserId()), "Ausência registrada.");

    [HttpPost] public async Task<IActionResult> CaptureBaseline(Guid boardId, string name) =>
        await Execute(() => _service.CaptureBaselineAsync(boardId, name, UserId()), "Baseline registrada.");

    [HttpPost] public async Task<IActionResult> AddDependency(Guid predecessor, Guid successor, string type, int lagDays) =>
        await Execute(() => _service.AddPortfolioDependencyAsync(predecessor, successor, type, lagDays, UserId()), "Dependência adicionada.");

    [HttpPost] public async Task<IActionResult> SaveTemplate(string name, string? description, Guid? boardId, Guid? teamId, int estimatedMinutes, string priority) =>
        await Execute(() => _service.SaveTemplateAsync(name, description, boardId, teamId, estimatedMinutes, priority, UserId()), "Modelo salvo.");

    [HttpPost] public async Task<IActionResult> SaveRecurring(Guid boardId, string title, string? description, string cadence,
        int intervalCount, DateTime nextRunAt, string timeZone, string targetStatus, string priority,
        Guid? templateId, Guid? assignedTo, int estimatedMinutes, int dueAfterDays, DateTime? endsAt, int? maxOccurrences) =>
        await Execute(() => _service.SaveRecurringRuleAsync(boardId, title, description, cadence, intervalCount, nextRunAt,
            timeZone, targetStatus, priority, templateId, assignedTo, estimatedMinutes, dueAfterDays,
            endsAt, maxOccurrences, UserId()), "Recorrência programada.");

    [HttpPost] public async Task<IActionResult> DeleteHoliday(Guid id) =>
        await Execute(() => _service.DeleteHolidayAsync(id), "Feriado removido.");

    [HttpPost] public async Task<IActionResult> CancelAbsence(Guid id) =>
        await Execute(() => _service.CancelAbsenceAsync(id), "Ausência cancelada.");

    [HttpPost] public async Task<IActionResult> DeleteDependency(Guid id) =>
        await Execute(() => _service.DeletePortfolioDependencyAsync(id), "Dependência removida.");

    [HttpPost] public async Task<IActionResult> ToggleTemplate(Guid id, bool active) =>
        await Execute(() => _service.ToggleTemplateAsync(id, active), active ? "Modelo reativado." : "Modelo desativado.");

    [HttpPost] public async Task<IActionResult> ToggleRecurring(Guid id, bool active) =>
        await Execute(() => _service.ToggleRecurringAsync(id, active), active ? "Recorrência retomada." : "Recorrência pausada.");

    [HttpPost] public async Task<IActionResult> UpdateRecurring(Guid id, string title, string? description, string cadence,
        int intervalCount, DateTime nextRunAt, string timeZone, string targetStatus, string priority,
        Guid? assignedTo, int estimatedMinutes, int dueAfterDays, DateTime? endsAt, int? maxOccurrences) =>
        await Execute(() => _service.UpdateRecurringRuleAsync(id, title, description, cadence, intervalCount,
            nextRunAt, timeZone, targetStatus, priority, assignedTo, estimatedMinutes, dueAfterDays, endsAt, maxOccurrences),
            "Recorrência atualizada.");

    private async Task<IActionResult> Execute(Func<Task> action, string success)
    {
        try { await action(); TempData["Success"] = success; }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
