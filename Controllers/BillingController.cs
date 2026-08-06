using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class BillingController : Controller
{
    private readonly BillingService _service;

    public BillingController(BillingService service) => _service = service;

    public async Task<IActionResult> Index(string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        var model = await _service.GetBillingAsync(userId, month);
        return model == null ? Forbid() : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SaveContract(ClientContract contract)
    {
        try
        {
            await _service.SaveContractAsync(contract);
            TempData["Success"] = "Contrato salvo.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ReviewLog(Guid logId, bool approve, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        await _service.ReviewTimeLogAsync(logId, userId, approve);
        TempData["Success"] = approve ? "Apontamento aprovado." : "Apontamento rejeitado.";
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> GenerateInvoice(Guid clientId, DateTime periodStart, DateTime periodEnd, DateTime? dueDate)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            var invoice = await _service.GenerateInvoiceAsync(clientId, userId, periodStart, periodEnd, dueDate);
            TempData["Success"] = $"Fatura {invoice.Reference} criada.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month = periodStart.ToString("yyyy-MM") });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateInvoice(Guid invoiceId, string status, string? month)
    {
        await _service.UpdateInvoiceStatusAsync(invoiceId, status);
        TempData["Success"] = "Situação da fatura atualizada.";
        return RedirectToAction(nameof(Index), new { month });
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
