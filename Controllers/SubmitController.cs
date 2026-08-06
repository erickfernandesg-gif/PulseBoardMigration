using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[AllowAnonymous]
public class SubmitController : Controller
{
    private readonly BoardService _boardService;

    public SubmitController(BoardService boardService)
    {
        _boardService = boardService;
    }

    [HttpGet("/Submit/{boardId:guid}")]
    public async Task<IActionResult> Index(Guid boardId)
    {
        var board = await _boardService.GetBoardByIdAsync(boardId);
        return board == null ? NotFound() : View(board);
    }

    [HttpPost("/Submit/{boardId:guid}")]
    public async Task<IActionResult> Create(
        Guid boardId,
        string title,
        string? description,
        string? requesterName,
        string? requesterEmail,
        string priority = "medium")
    {
        var details = description;
        if (!string.IsNullOrWhiteSpace(requesterName) || !string.IsNullOrWhiteSpace(requesterEmail))
        {
            details = $"{description}\n\nSolicitante: {requesterName} <{requesterEmail}>".Trim();
        }

        var task = await _boardService.CreateTaskAsync(new PulseTask
        {
            BoardId = boardId,
            Title = title,
            Description = details,
            Status = "backlog",
            Priority = priority
        }, anonymous: true);

        if (task == null)
        {
            ModelState.AddModelError(string.Empty, "Não foi possível enviar a solicitação.");
            var board = await _boardService.GetBoardByIdAsync(boardId);
            return board == null ? NotFound() : View("Index", board);
        }

        ViewData["Success"] = true;
        return View("Success");
    }
}
