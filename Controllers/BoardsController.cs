using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using PulseBoardMigration.Models;

namespace PulseBoardMigration.Controllers
{
    [Authorize]
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
        public async Task<IActionResult> Create(string name, string description)
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
                return NotFound();
            }

            var tasks = await _boardService.GetTasksByBoardIdAsync(id);

            // 2. Prepara as colunas para o Modal não quebrar (mapeando de board.Settings)
            var columns = board.Settings != null
                ? board.Settings.Select(s => new Column { Id = s.Id, Title = s.Title }).ToList()
                : new List<Column>();

            // 3. Monta o pacote de dados
            var viewModel = new BoardDetailsViewModel
            {
                Board = board,
                Tasks = tasks,
                Columns = columns
            };

            // Simula uma lista de usuários da equipe (substitua futuramente por uma chamada ao AuthService para trazer do Supabase)
            ViewData["Users"] = new List<User>();

            // 4. Entrega o pacote para a View desenhar o Kanban
            return View(viewModel);
        }

        // ==========================================
        // ENDPOINTS AJAX (Não recarregam a página)
        // ==========================================

        [HttpPost]
        public async Task<IActionResult> CreateTask(Guid boardId, string title, string description, string columnId, string priority, DateTime? startDate, DateTime? dueDate, Guid? assigneeId, string department, string riskLevel, int? storyPoints, string tags)
        {
            try
            {
                if (boardId == Guid.Empty || string.IsNullOrEmpty(title))
                {
                    return Json(new { success = false, message = "Dados inválidos." });
                }

                // Passa os novos parâmetros para o serviço
                var newTask = await _boardService.CreateTaskAsync(boardId, title, description, columnId, priority, startDate, dueDate, assigneeId, department, riskLevel, storyPoints, tags);

                if (newTask != null)
                    return Json(new { success = true, data = newTask });

                return Json(new { success = false, message = "Falha ao criar tarefa no banco." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateTaskDetails(Guid taskId, string title, string description, string columnId, string priority, DateTime? startDate, DateTime? dueDate, Guid? assigneeId, string department, string riskLevel, int? storyPoints, string tags)
        {
            try
            {
                var updatedTask = await _boardService.UpdateTaskDetailsAsync(taskId, title, description, columnId, priority, startDate, dueDate, assigneeId, department, riskLevel, storyPoints, tags);

                if (updatedTask != null)
                    return Json(new { success = true, data = updatedTask });

                return Json(new { success = false, message = "Falha ao atualizar tarefa." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // Usado pelo Drag and Drop para mover cards de uma coluna para a outra silenciosamente
        [HttpPost]
        public async Task<IActionResult> MoveTask(Guid taskId, string newColumnId)
        {
            if (taskId == Guid.Empty || string.IsNullOrEmpty(newColumnId))
            {
                return Json(new { success = false, message = "ID da tarefa ou coluna inválido." });
            }

            var success = await _boardService.UpdateTaskStatusAsync(taskId, newColumnId);

            return Json(new { success = success });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTask(Guid taskId)
        {
            try
            {
                var success = await _boardService.DeleteTaskAsync(taskId);
                return Json(new { success = success });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}