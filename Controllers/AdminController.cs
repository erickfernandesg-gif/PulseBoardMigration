using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;
using PulseBoardMigration.Security;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize(Policy = PulsePolicies.AdminOnly)]
public class AdminController : Controller
{
    private readonly WorkspaceService _workspaceService;

    public AdminController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    public async Task<IActionResult> Index()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return Challenge();
        }

        var model = await _workspaceService.GetAdminAsync(id);
        return model.IsManager ? View(model) : Forbid();
    }

    [HttpPost]
    public async Task<IActionResult> SaveTeam(Team team)
    {
        try { await _workspaceService.SaveTeamAsync(team); TempData["Success"] = "Equipe salva."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTeam(Guid id)
    {
        try { await _workspaceService.DeleteTeamAsync(id); TempData["Success"] = "Equipe excluída."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SaveClient(ClientAccount account)
    {
        try { await _workspaceService.SaveClientAsync(account); TempData["Success"] = "Cliente salvo."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteClient(Guid id)
    {
        try { await _workspaceService.DeleteClientAsync(id); TempData["Success"] = "Cliente excluído."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateUser(
        Guid id,
        string fullName,
        string role,
        Guid? teamId,
        decimal hourlyRate)
    {
        try { await _workspaceService.UpdateUserAsync(id, fullName, role, teamId, hourlyRate); TempData["Success"] = "Pessoa atualizada."; }
        catch (Exception ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(
        string email,
        string password,
        string fullName,
        string role,
        Guid? teamId,
        decimal hourlyRate)
    {
        try
        {
            await _workspaceService.CreateEmployeeAsync(
                email, password, fullName, role, teamId, hourlyRate);
            TempData["Success"] = "Usuário criado com sucesso.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Não foi possível criar o usuário: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId)) return Unauthorized();
            if (id == actorId) throw new InvalidOperationException("Você não pode desativar o próprio usuário.");
            await _workspaceService.DeactivateEmployeeAsync(id, actorId);
            TempData["Success"] = "Usuário removido.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Não foi possível remover o usuário: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ReactivateUser(Guid id)
    {
        try
        {
            await _workspaceService.ReactivateEmployeeAsync(id);
            TempData["Success"] = "Usuário reativado.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Não foi possível reativar o usuário: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
