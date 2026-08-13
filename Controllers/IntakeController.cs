using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

public class IntakeController : Controller
{
    private readonly BoardOperationsService _service;
    public IntakeController(BoardOperationsService service) => _service = service;

    [AllowAnonymous]
    [HttpGet("solicitacao/{token}")]
    public async Task<IActionResult> Form(string token)
    {
        var form = await _service.GetPublicFormAsync(token);
        return form is not { IsActive: true } ? NotFound() : View(new IntakePublicViewModel
        { Token = token, Title = form.Title, Description = form.Description, RequireEmail = form.RequireEmail });
    }

    [AllowAnonymous]
    [HttpPost("solicitacao/{token}")]
    public async Task<IActionResult> Submit(string token, string title, string? description, string requesterName, string? requesterEmail)
    {
        try { ViewBag.TaskId = await _service.SubmitIntakeAsync(token, title, description, requesterName, requesterEmail); return View("Success"); }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            var form = await _service.GetPublicFormAsync(token);
            return form == null ? NotFound() : View("Form", new IntakePublicViewModel
            { Token = token, Title = form.Title, Description = form.Description, RequireEmail = form.RequireEmail });
        }
    }
}
