using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class ExecutiveController : Controller
{
    private readonly ReportingService _reportingService;

    public ExecutiveController(ReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<IActionResult> Index(string? fromMonth, string? toMonth)
    {
        return View(await _reportingService.GetExecutiveAsync(fromMonth, toMonth));
    }
}
