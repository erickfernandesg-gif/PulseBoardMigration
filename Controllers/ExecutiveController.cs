using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using PulseBoardMigration.Security;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulsePolicies.ManagerOrAdmin)]
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
