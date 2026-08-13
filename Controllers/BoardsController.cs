using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Models;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class BoardsController : Controller
{
    private readonly BoardService _boardService;
    private readonly ILogger<BoardsController> _logger;

    public BoardsController(BoardService boardService, ILogger<BoardsController> logger)
    {
        _boardService = boardService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _boardService.GetBoardsAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Create(string name, string? description, DateTime? plannedStart, DateTime? plannedEnd, decimal? budgetAmount)
    {
        if (string.IsNullOrWhiteSpace(name) || !TryUserId(out var userId))
        {
            TempData["Error"] = "Não foi possível criar o quadro.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await _boardService.CreateBoardAsync(name, description, userId, plannedStart, plannedEnd, budgetAmount);
            TempData["Success"] = "Quadro criado com sucesso.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao criar quadro para {UserId}", userId);
            TempData["Error"] = exception.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var model = await _boardService.GetBoardDetailsAsync(id);
        if (model != null && TryUserId(out var userId)) model.CurrentUserId = userId;
        return model == null ? NotFound() : View(model);
    }

    [HttpPost]
    public async Task<IActionResult> UpdateBoard(
        Guid boardId,
        string name,
        string? description,
        string status,
        string health,
        DateTime? plannedStart,
        DateTime? plannedEnd,
        decimal? budgetAmount)
    {
        try
        {
            var updated = await _boardService.UpdateBoardAsync(boardId, name, description, status, health, plannedStart, plannedEnd, budgetAmount);
            TempData[updated ? "Success" : "Error"] = updated ? "Quadro atualizado." : "Quadro não encontrado ou sem permissão.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteBoard(Guid boardId)
    {
        try
        {
            var archived = await _boardService.DeleteBoardAsync(boardId);
            TempData[archived ? "Success" : "Error"] = archived ? "Quadro arquivado. O histórico foi preservado." : "Quadro não encontrado ou sem permissão.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> RestoreBoard(Guid boardId)
    {
        try
        {
            var restored = await _boardService.RestoreBoardAsync(boardId);
            TempData[restored ? "Success" : "Error"] = restored ? "Quadro restaurado." : "Quadro não encontrado ou sem permissão.";
        }
        catch (Exception exception) { TempData["Error"] = exception.Message; }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask(
        Guid boardId,
        string title,
        string? description,
        string columnId,
        string priority,
        DateTime? startDate,
        DateTime? dueDate,
        Guid? assignedTo,
        Guid? clientId,
        string? targetMonth,
        int estimatedHours,
        int estimatedMinutes,
        List<Guid>? collaboratorIds)
    {
        var validationError = ValidateTaskInput(
            boardId, title, columnId, priority, startDate, dueDate,
            targetMonth, estimatedHours, estimatedMinutes);
        if (validationError != null)
        {
            return BadRequest(new { success = false, message = validationError });
        }

        try
        {
            if (!TryUserId(out var currentUserId)) return Unauthorized();
            var created = await _boardService.CreateTaskAsync(new PulseTask
            {
                BoardId = boardId,
                Title = title,
                Description = description,
                Status = columnId,
                Priority = priority,
                StartDate = startDate,
                DueDate = dueDate,
                AssignedTo = assignedTo,
                AccountableOwnerId = currentUserId,
                CreatedBy = currentUserId,
                WorkflowState = assignedTo.HasValue ? "inbox" : "waiting_external",
                ClientId = clientId,
                TargetMonth = targetMonth,
                EstimatedMinutes = checked(estimatedHours * 60 + estimatedMinutes)
            }, collaboratorIds ?? []);

            return Json(new
            {
                success = created != null,
                message = created == null ? "Falha ao criar a tarefa." : null,
                data = created
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { success = false, message = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Erro ao criar tarefa no quadro {BoardId}", boardId);
            return StatusCode(500, new { success = false, message = "Não foi possível criar a tarefa. Tente novamente." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateTask(
        Guid taskId,
        Guid boardId,
        long expectedVersion,
        string title,
        string? description,
        string columnId,
        string priority,
        DateTime? startDate,
        DateTime? dueDate,
        Guid? assignedTo,
        Guid? clientId,
        string? targetMonth,
        int estimatedHours,
        int estimatedMinutes,
        int? slaMinutes,
        decimal? plannedValue,
        string? customFieldsJson,
        bool isBlocked,
        string? blockerReason,
        List<Guid>? collaboratorIds)
    {
        var validationError = taskId == Guid.Empty
            ? "Tarefa inválida."
            : ValidateTaskInput(
                boardId, title, columnId, priority, startDate, dueDate,
                targetMonth, estimatedHours, estimatedMinutes);
        if (validationError == null && isBlocked && string.IsNullOrWhiteSpace(blockerReason))
        {
            validationError = "Informe o motivo do bloqueio.";
        }
        if (validationError != null)
        {
            return BadRequest(new { success = false, message = validationError });
        }

        try
        {
            Dictionary<string, object?> customFields;
            try
            {
                customFields = string.IsNullOrWhiteSpace(customFieldsJson)
                    ? []
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(customFieldsJson) ?? [];
            }
            catch (System.Text.Json.JsonException)
            {
                return BadRequest(new { success = false, message = "Os campos personalizados devem estar em JSON válido." });
            }
            var updated = await _boardService.UpdateTaskAsync(new PulseTask
            {
                Id = taskId,
                RowVersion = expectedVersion,
                BoardId = boardId,
                Title = title,
                Description = description,
                Status = columnId,
                Priority = priority,
                StartDate = startDate,
                DueDate = dueDate,
                AssignedTo = assignedTo,
                ClientId = clientId,
                TargetMonth = targetMonth,
                EstimatedMinutes = checked(estimatedHours * 60 + estimatedMinutes),
                SlaMinutes = slaMinutes.HasValue ? Math.Max(0, slaMinutes.Value) : null,
                PlannedValue = plannedValue.HasValue ? Math.Max(0, plannedValue.Value) : null,
                CustomFields = customFields,
                IsBlocked = isBlocked,
                BlockerReason = blockerReason
            }, collaboratorIds ?? []);

            return Json(new
            {
                success = updated != null,
                message = updated == null ? "Tarefa não encontrada ou sem permissão para edição." : null,
                data = updated
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { success = false, message = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Erro ao atualizar a tarefa {TaskId}", taskId);
            return StatusCode(500, new { success = false, message = "Não foi possível salvar as alterações. Tente novamente." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> MoveTask(Guid taskId, string newColumnId, int positionIndex = 0)
    {
        try
        {
            var success = taskId != Guid.Empty && !string.IsNullOrWhiteSpace(newColumnId) &&
                          await _boardService.MoveTaskAsync(taskId, newColumnId, positionIndex);
            return Json(new { success, message = success ? null : "Não foi possível mover a tarefa." });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Movimentação rejeitada para {TaskId}", taskId);
            return BadRequest(new { success = false, message = exception.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateTaskSchedule(Guid taskId, DateTime startDate, DateTime dueDate)
    {
        try
        {
            var success = await _boardService.UpdateTaskScheduleAsync(taskId, startDate, dueDate);
            return success
                ? Json(new { success = true })
                : BadRequest(new { success = false, message = "Tarefa não encontrada ou sem permissão." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { success = false, message = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao reagendar tarefa {TaskId} pelo Gantt", taskId);
            return StatusCode(500, new { success = false, message = "Não foi possível atualizar o cronograma." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTask(Guid taskId)
    {
        try { return Json(new { success = await _boardService.DeleteTaskAsync(taskId) }); }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> RestoreTask(Guid taskId)
    {
        try { return Json(new { success = await _boardService.RestoreTaskAsync(taskId) }); }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> AddComment(Guid taskId, string? content, List<IFormFile>? images,
        Guid? replyToId, List<Guid>? mentionedUserIds)
    {
        if (!TryUserId(out var userId))
        {
            return Json(new { success = false, message = "Comentário inválido." });
        }

        try
        {
            var uploads = await ReadChatImagesAsync(images ?? []);
            var comment = await _boardService.AddCommentAsync(taskId, userId, content, uploads,
                replyToId: replyToId, mentionedUserIds: mentionedUserIds ?? []);
            return Json(new { success = comment != null, data = comment });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { success = false, message = exception.Message });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha ao adicionar mensagem na tarefa {TaskId}", taskId);
            return StatusCode(500, new { success = false, message = "Não foi possível enviar a mensagem." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateSubtask(Guid parentTaskId, string title, Guid? assignedTo,
        DateTime? dueDate, int estimatedMinutes)
    {
        if (!TryUserId(out var userId) || string.IsNullOrWhiteSpace(title))
            return BadRequest(new { success = false, message = "Informe o título da subtarefa." });
        try
        {
            if (title.Trim().Length > 200) throw new InvalidOperationException("O título deve ter no máximo 200 caracteres.");
            var task = await _boardService.CreateSubtaskAsync(parentTaskId, userId, title, assignedTo, dueDate, estimatedMinutes);
            return Json(new { success = task != null, data = task });
        }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateComment(Guid commentId, string content)
    {
        if (!TryUserId(out var userId) || string.IsNullOrWhiteSpace(content) || content.Trim().Length > 5000)
        {
            return Json(new { success = false });
        }

        return Json(new
        {
            success = await _boardService.UpdateCommentAsync(commentId, userId, content)
        });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteComment(Guid commentId)
    {
        if (!TryUserId(out var userId))
        {
            return Json(new { success = false });
        }

        return Json(new
        {
            success = await _boardService.DeleteCommentAsync(commentId, userId)
        });
    }

    [HttpGet]
    public async Task<IActionResult> CommentAttachment(Guid id)
    {
        var attachment = await _boardService.GetCommentAttachmentAsync(id);
        if (attachment == null) return NotFound();
        Response.Headers.CacheControl = "private,max-age=300";
        return File(attachment.Content, attachment.ContentType, enableRangeProcessing: true);
    }

    [HttpPost]
    public async Task<IActionResult> AddTaskFile(Guid taskId, IFormFile file, string? description, Guid? previousVersionId)
    {
        if (!TryUserId(out var userId) || file == null) return BadRequest(new { success = false });
        if (file.Length <= 0 || file.Length > 25 * 1024 * 1024)
            return BadRequest(new { success = false, message = "O arquivo deve ter no máximo 25 MB." });
        if (description?.Length > 500) return BadRequest(new { success = false, message = "A descrição deve ter no máximo 500 caracteres." });
        try
        {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var created = await _boardService.AddTaskFileAsync(taskId, userId, file.FileName,
            string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            stream.ToArray(), description, previousVersionId);
        return Json(new { success = created != null, data = created });
        }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> TaskFile(Guid id)
    {
        var file = await _boardService.GetTaskFileAsync(id);
        return file == null ? NotFound() : File(file.Content, file.ContentType, file.FileName, true);
    }

    [HttpPost]
    public async Task<IActionResult> AddTimeLog(
        Guid taskId,
        int hours,
        int minutes,
        DateTime? logDate,
        string? description,
        bool isBillable = false)
    {
        if (!TryUserId(out var userId))
        {
            return Json(new { success = false });
        }

        var total = Math.Max(0, hours) * 60 + Math.Clamp(minutes, 0, 59);
        if (total <= 0)
        {
            return Json(new { success = false, message = "Informe um tempo maior que zero." });
        }

        try
        {
            if (description?.Length > 1000) throw new InvalidOperationException("A descrição deve ter no máximo 1.000 caracteres.");
            var log = await _boardService.AddTimeLogAsync(new TimeLog
            {
                TaskId = taskId, UserId = userId, Minutes = total,
                LogDate = logDate ?? DateTime.UtcNow.Date, Description = description, IsBillable = isBillable
            });
            return Json(new { success = log != null, data = log });
        }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> AddChecklistItem(Guid taskId, string title, int position)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 300)
        {
            return BadRequest(new { success = false, message = "O item deve ter entre 1 e 300 caracteres." });
        }
        try
        {
            var item = await _boardService.AddChecklistItemAsync(taskId, title, position);
            return Json(new { success = item != null, data = item });
        }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> ToggleChecklistItem(Guid id, bool completed)
    {
        try { return Json(new { success = await _boardService.ToggleChecklistItemAsync(id, completed) }); }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteChecklistItem(Guid id)
    {
        try { return Json(new { success = await _boardService.DeleteChecklistItemAsync(id) }); }
        catch (Exception exception) { return BadRequest(new { success = false, message = exception.Message }); }
    }

    private bool TryUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private static async Task<List<CommentImageUpload>> ReadChatImagesAsync(IReadOnlyList<IFormFile> images)
    {
        const long maxFileSize = 8 * 1024 * 1024;
        const long maxTotalSize = 20 * 1024 * 1024;
        if (images.Count > 4) throw new InvalidOperationException("Envie no máximo 4 imagens por mensagem.");
        if (images.Sum(image => image.Length) > maxTotalSize)
            throw new InvalidOperationException("O conjunto de imagens deve ter no máximo 20 MB.");

        var result = new List<CommentImageUpload>(images.Count);
        foreach (var image in images)
        {
            if (image.Length <= 0 || image.Length > maxFileSize)
                throw new InvalidOperationException("Cada imagem deve ter no máximo 8 MB.");
            var contentType = image.ContentType.ToLowerInvariant();
            if (contentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif"))
                throw new InvalidOperationException("Use imagens JPG, PNG, WEBP ou GIF.");

            await using var stream = new MemoryStream();
            await image.CopyToAsync(stream);
            var bytes = stream.ToArray();
            if (!HasValidImageSignature(contentType, bytes))
                throw new InvalidOperationException($"O arquivo {Path.GetFileName(image.FileName)} não é uma imagem válida.");
            result.Add(new CommentImageUpload(Path.GetFileName(image.FileName), contentType, bytes));
        }
        return result;
    }

    private static bool HasValidImageSignature(string contentType, byte[] bytes) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
        "image/png" => bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
        "image/gif" => bytes.Length >= 6 && (System.Text.Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a"),
        "image/webp" => bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP",
        _ => false
    };

    private string? ValidateTaskInput(
        Guid boardId,
        string? title,
        string? columnId,
        string? priority,
        DateTime? startDate,
        DateTime? dueDate,
        string? targetMonth,
        int estimatedHours,
        int estimatedMinutes)
    {
        if (!ModelState.IsValid)
        {
            return "Há campos com valores inválidos. Revise o formulário.";
        }
        if (boardId == Guid.Empty)
        {
            return "Quadro inválido.";
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Informe o título da tarefa.";
        }
        if (title.Trim().Length > 200)
        {
            return "O título deve ter no máximo 200 caracteres.";
        }
        if (string.IsNullOrWhiteSpace(columnId))
        {
            return "Selecione uma etapa.";
        }
        if (priority is not ("low" or "medium" or "high" or "critical"))
        {
            return "Prioridade inválida.";
        }
        if (startDate.HasValue && dueDate.HasValue && dueDate.Value.Date < startDate.Value.Date)
        {
            return "O prazo não pode ser anterior à data de início.";
        }
        if (!string.IsNullOrWhiteSpace(targetMonth) &&
            !System.Text.RegularExpressions.Regex.IsMatch(targetMonth, @"^\d{4}-(0[1-9]|1[0-2])$"))
        {
            return "Ciclo inválido.";
        }
        if (estimatedHours is < 0 or > 100000 || estimatedMinutes is < 0 or > 59)
        {
            return "Informe uma estimativa válida (minutos entre 0 e 59).";
        }

        return null;
    }
}
