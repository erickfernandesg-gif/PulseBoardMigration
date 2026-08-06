using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
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
        await _workspaceService.SaveTeamAsync(team);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTeam(Guid id)
    {
        await _workspaceService.DeleteTeamAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SaveClient(ClientAccount account)
    {
        await _workspaceService.SaveClientAsync(account);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteClient(Guid id)
    {
        await _workspaceService.DeleteClientAsync(id);
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
        await _workspaceService.UpdateUserAsync(id, fullName, role, teamId, hourlyRate);
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
            await _workspaceService.DeleteEmployeeAsync(id);
            TempData["Success"] = "Usuário removido.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Não foi possível remover o usuário: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
