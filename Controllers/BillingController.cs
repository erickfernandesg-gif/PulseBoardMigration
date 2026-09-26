using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;
using PulseBoardMigration.Security;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulsePolicies.FinanceAccess)]
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
    public async Task<IActionResult> SaveContract(ClientContract contract, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            await _service.SaveContractAsync(contract, userId);
            TempData["Success"] = "Contrato salvo.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteContract(Guid contractId, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            await _service.DeleteContractAsync(contractId, userId);
            TempData["Success"] = "Contrato excluído.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> ReviewLog(Guid logId, bool approve, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            await _service.ReviewTimeLogAsync(logId, userId, approve);
            TempData["Success"] = approve ? "Apontamento aprovado." : "Apontamento rejeitado.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> DeletePendingLog(Guid logId, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            await _service.DeletePendingTimeLogAsync(logId, userId);
            TempData["Success"] = "Apontamento pendente excluído.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> ReopenLog(Guid logId, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            await _service.ReopenTimeLogAsync(logId, userId);
            TempData["Success"] = "Apontamento reaberto para revisão. Agora ele pode ser revisado ou excluído.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> ReverseInvoiceAndDeleteLog(Guid logId, string? month)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            await _service.ReverseInvoiceAndDeleteTimeLogAsync(logId, userId);
            TempData["Success"] = "Fatura estornada e apontamento excluído. Os demais itens da fatura voltaram para a fila de cobrança.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCancelledInvoice(Guid invoiceId, string? month)
    {
        try
        {
            await _service.DeleteCancelledInvoiceAsync(invoiceId);
            TempData["Success"] = "Fatura cancelada, seus itens e eventos foram excluídos.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> CleanDemoScenario(Guid invoiceId, string? month)
    {
        try
        {
            await _service.CleanDemoScenarioAsync(invoiceId);
            TempData["Success"] = "Cenário DEMO removido: fatura, contrato, cliente e projeto de teste.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    [HttpPost]
    public async Task<IActionResult> GenerateInvoice(Guid clientId, Guid boardId, DateTime periodStart, DateTime periodEnd, DateTime? dueDate)
    {
        if (!UserId(out var userId)) return Unauthorized();
        try
        {
            var invoice = await _service.GenerateInvoiceAsync(clientId, boardId, userId, periodStart, periodEnd, dueDate);
            TempData["Success"] = $"Fatura {invoice.Reference} criada.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month = periodStart.ToString("yyyy-MM") });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateInvoice(Guid invoiceId, string status, string? month)
    {
        try
        {
            await _service.UpdateInvoiceStatusAsync(invoiceId, status);
            TempData["Success"] = status == "cancelled"
                ? "Rascunho cancelado; as horas voltaram para faturamento."
                : "Situação da fatura atualizada.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index), new { month });
    }

    private bool UserId(out Guid id) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);
}
