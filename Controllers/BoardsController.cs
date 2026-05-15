using Microsoft.AspNetCore.Authorization; // 1. Adicione este using
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using System.Threading.Tasks;
using PulseBoardMigration.Models;

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
        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            // 1. Pega os dados do banco
            var board = await _boardService.GetBoardByIdAsync(id);
            if (board == null)
            {
                return NotFound(); // Se tentarem acessar um quadro deletado, dá erro 404
            }

            var tasks = await _boardService.GetTasksByBoardIdAsync(id);

            // 2. Monta o pacote de dados
            var viewModel = new BoardDetailsViewModel
            {
                Board = board,
                Tasks = tasks
            };

            // 3. Entrega o pacote para a View desenhar o Kanban
            return View(viewModel);
        }
        [HttpPost]
        public async Task<IActionResult> UpdateTaskStatus([FromBody] UpdateTaskStatusRequest request)
        {
            if (request == null || request.TaskId == Guid.Empty || string.IsNullOrEmpty(request.NewStatus))
            {
                return BadRequest(new { success = false, message = "Dados não chegaram no C#." });
            }

            // Chama o serviço e recebe o erro (se houver)
            var errorMessage = await _boardService.UpdateTaskStatusAsync(request.TaskId, request.NewStatus);

            if (errorMessage == null)
            {
                return Json(new { success = true });
            }

            // Retorna o ERRO REAL do banco de dados!
            return StatusCode(500, new { success = false, message = errorMessage });
        }
        [HttpPost]
        public async Task<IActionResult> CreateTask(Guid boardId, string title, string description, string status, string priority, DateTime? dueDate)
        {
            if (boardId != Guid.Empty && !string.IsNullOrEmpty(title))
            {
                await _boardService.CreateTaskAsync(boardId, title, description, status, priority, dueDate);
            }

            // Recarrega a página do Kanban atual para exibir o novo card
            return RedirectToAction("Details", new { id = boardId });
        }
        [HttpPost]
        public async Task<IActionResult> UpdateTaskDetails(Guid boardId, Guid taskId, string title, string description, string priority, DateTime? dueDate)
        {
            await _boardService.UpdateTaskDetailsAsync(taskId, title, description, priority, dueDate);
            return RedirectToAction("Details", new { id = boardId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTask(Guid boardId, Guid taskId)
        {
            await _boardService.DeleteTaskAsync(taskId);
            return RedirectToAction("Details", new { id = boardId });
        }
        [HttpPost]
        public async Task<IActionResult> UpdateTaskStatus(string taskId, string newColumnId)
        {
            // Chama o BoardService para atualizar no Supabase
            // Retorna JSON para o JavaScript lidar na tela
            return Json(new { success = true });
        }

        //[HttpPost]
        //public async Task<IActionResult> CreateTask([FromBody] TaskDto taskData)
        //{
        //    // Cria tarefa e retorna os dados recém criados
        //    return Json(new { success = true, data = newTask });
        //}
    }
    // Adicione isto no final do arquivo, antes da última chave "}"
    public class UpdateTaskStatusRequest
    {
        public Guid TaskId { get; set; }
        public string NewStatus { get; set; }
    }
}