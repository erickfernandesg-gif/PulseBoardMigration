using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using PulseBoardMigration.Services;
using Microsoft.AspNetCore.Authorization; // 1. Adicione este using

namespace PulseBoardMigration.Controllers
{
    [Authorize] // 2. Adicione esta tag! Ela tranca todas as rotas deste Controller.
    public class BoardsController : Controller
    {
        private readonly BoardService _boardService;

        public BoardsController(BoardService boardService)
        {
            _boardService = boardService;
        }

        public async Task<IActionResult> Index()
        {
            var boards = await _boardService.GetBoardsAsync();
            return View(boards);
        }
        [HttpPost]
        public async Task<IActionResult> Create(string name, string description) // Mudou de title para name
        {
            var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (Guid.TryParse(userIdString, out Guid userId))
            {
                await _boardService.CreateBoardAsync(name, description, userId);
            }

            return RedirectToAction("Index");
        }
    }
}