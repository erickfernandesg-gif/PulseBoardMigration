using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class ScheduleController : Controller
{
    private readonly WorkManagementService _service;

    public ScheduleController(WorkManagementService service) => _service = service;

    public async Task<IActionResult> Index(DateTime? from, DateTime? to)
    {
        return View(await _service.GetCompanyScheduleAsync(from, to));
    }

    [HttpPost]
    public async Task<IActionResult> AddMilestone(Guid boardId, string title, DateTime dueDate)
    {
        if (boardId == Guid.Empty || string.IsNullOrWhiteSpace(title))
        {
            TempData["Error"] = "Preencha os dados do marco.";
        }
        else
        {
            await _service.AddMilestoneAsync(boardId, title, dueDate);
            TempData["Success"] = "Marco adicionado ao cronograma.";
        }
        return RedirectToAction(nameof(Index));
    }
}
