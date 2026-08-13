using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using PulseBoardMigration.Security;
using System.Text;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulsePolicies.ManagerOrAdmin)]
public class ReportsController : Controller
{
    private readonly ReportingService _reportingService;

    public ReportsController(ReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<IActionResult> Index(string? month, Guid? teamId, Guid? userId)
    {
        return View(await _reportingService.GetReportsAsync(month, teamId, userId));
    }

    [HttpGet]
    public async Task<IActionResult> Export(string? month, Guid? teamId, Guid? userId)
    {
        var model = await _reportingService.GetReportsAsync(month, teamId, userId);
        var csv = new StringBuilder();
        csv.AppendLine("Projeto;Tarefas;Concluídas;Bloqueadas;Horas;Custo");
        foreach (var row in model.Rows)
        {
            csv.AppendLine(
                $"\"{row.BoardName.Replace("\"", "\"\"")}\";{row.TotalTasks};{row.CompletedTasks};" +
                $"{row.BlockedTasks};{row.LoggedMinutes / 60m:0.00};{row.Cost:0.00}");
        }

        return File(
            Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv",
            $"PulseBoard_Relatorio_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
