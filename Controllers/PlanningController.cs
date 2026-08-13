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

    public async Task<IActionResult> Index() => View(await _service.GetPlanningAsync());

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
        int intervalCount, DateTime nextRunAt, Guid? assignedTo, int estimatedMinutes, int dueAfterDays) =>
        await Execute(() => _service.SaveRecurringRuleAsync(boardId, title, description, cadence, intervalCount, nextRunAt,
            assignedTo, estimatedMinutes, dueAfterDays, UserId()), "Recorrência programada.");

    private async Task<IActionResult> Execute(Func<Task> action, string success)
    {
        try { await action(); TempData["Success"] = success; }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
