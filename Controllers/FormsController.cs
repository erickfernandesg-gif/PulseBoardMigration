using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class FormsController : Controller
{
    private readonly BoardService _boardService;

    public FormsController(BoardService boardService)
    {
        _boardService = boardService;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _boardService.GetBoardsAsync());
    }
}
