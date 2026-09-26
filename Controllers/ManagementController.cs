using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using PulseBoardMigration.Security;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulsePolicies.ManagerOrAdmin)]
public class ManagementController : Controller
{
    private readonly WorkManagementService _service;

    public ManagementController(WorkManagementService service) => _service = service;

    public async Task<IActionResult> Index()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var model = await _service.GetManagementAsync(userId);
        return model == null ? Forbid() : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SaveCapacity(Guid userId, decimal weeklyHours)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
        try
        {
            await _service.SaveWorkScheduleAsync(actorId, userId, (int)Math.Round(Math.Clamp(weeklyHours, 0, 168) * 60));
            TempData["Success"] = "Capacidade semanal atualizada.";
        }
        catch (Exception exception)
        {
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
