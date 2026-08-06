using PulseBoardMigration.Models;

#pragma warning disable CS8603 // Postgrest Set<T?> expression trees report nullable false positives.
namespace PulseBoardMigration.Services;

public class BoardService
{
    private const string TaskChatBucket = "task-chat";
    private readonly SupabaseClientFactory _clientFactory;
    private readonly ILogger<BoardService> _logger;

    public BoardService(SupabaseClientFactory clientFactory, ILogger<BoardService> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<List<Board>> GetBoardsAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<Board>().Get();
        return response.Models.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public async Task<Board?> CreateBoardAsync(string name, string? description, Guid ownerId, DateTime? plannedStart = null, DateTime? plannedEnd = null, decimal? budgetAmount = null)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = new Board
        {
            Name = name.Trim(),
            Description = description?.Trim(),
            OwnerId = ownerId,
            Status = "active",
            Health = "on_track",
            PlannedStart = plannedStart?.Date,
            PlannedEnd = plannedEnd?.Date,
            BudgetAmount = budgetAmount.HasValue ? Math.Max(0, budgetAmount.Value) : null,
            CreatedAt = DateTime.UtcNow,
            Settings =
            [
                new() { Id = "backlog", Title = "Caixa de Entrada" },
                new() { Id = "todo", Title = "A Fazer" },
                new() { Id = "in-progress", Title = "Em Execução" },
                new() { Id = "homologation", Title = "Homologação" },
                new() { Id = "done", Title = "Concluído" }
            ]
        };

        var response = await client.From<Board>().Insert(board);
        return response.Models.FirstOrDefault();
    }

    public async Task<Board?> GetBoardByIdAsync(Guid boardId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        return await client.From<Board>().Where(b => b.Id == boardId).Single();
    }

    public async Task<List<PulseTask>> GetTasksByBoardIdAsync(Guid boardId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<PulseTask>().Where(t => t.BoardId == boardId).Get();
        return response.Models
            .OrderBy(t => t.PositionIndex)
            .ThenBy(t => t.CreatedAt)
            .ToList();
    }

    public async Task<BoardDetailsViewModel?> GetBoardDetailsAsync(Guid boardId)
    {
        var board = await GetBoardByIdAsync(boardId);
        if (board == null)
        {
            return null;
        }

        var client = await _clientFactory.CreateForCurrentUserAsync();
        var tasks = await client.From<PulseTask>().Where(t => t.BoardId == boardId).Get();
        var profiles = await client.From<Profile>().Get();
        var clients = await client.From<ClientAccount>().Get();
        var taskIds = tasks.Models.Select(t => t.Id).ToHashSet();

        var collaborators = await client.From<TaskCollaborator>().Get();
        var comments = await client.From<TaskComment>().Get();
        var commentAttachments = new List<TaskCommentAttachment>();
        try
        {
            var attachmentResponse = await client.From<TaskCommentAttachment>().Get();
            commentAttachments = attachmentResponse.Models;
        }
        catch (Postgrest.Exceptions.PostgrestException exception) when (IsMissingChatAttachmentTable(exception))
        {
            _logger.LogWarning("Tabela task_comment_attachments ainda não instalada; detalhes carregados sem anexos.");
        }
        var timeLogs = await client.From<TimeLog>().Get();
        var checklists = await client.From<TaskChecklist>().Get();
        var activity = await client.From<ActivityLog>().Where(a => a.BoardId == boardId).Get();
        var assignments = await client.From<TaskAssignment>().Get();
        var dependencies = await client.From<TaskDependency>().Get();

        var settings = board.Settings?.Count > 0
            ? board.Settings
            :
            [
                new() { Id = "todo", Title = "A Fazer" },
                new() { Id = "in-progress", Title = "Em Execução" },
                new() { Id = "done", Title = "Concluído" }
            ];

        return new BoardDetailsViewModel
        {
            Board = board,
            Tasks = tasks.Models.OrderBy(t => t.PositionIndex).ThenBy(t => t.CreatedAt).ToList(),
            Columns = settings.Select(s => new Column { Id = s.Id, Title = s.Title }).ToList(),
            Profiles = profiles.Models.OrderBy(p => p.FullName ?? p.Email).ToList(),
            Clients = clients.Models.OrderBy(c => c.Name).ToList(),
            Collaborators = collaborators.Models.Where(x => taskIds.Contains(x.TaskId)).ToList(),
            Comments = comments.Models.Where(x => taskIds.Contains(x.TaskId)).OrderBy(x => x.CreatedAt).ToList(),
            CommentAttachments = commentAttachments.Where(x => taskIds.Contains(x.TaskId)).OrderBy(x => x.CreatedAt).ToList(),
            TimeLogs = timeLogs.Models.Where(x => taskIds.Contains(x.TaskId)).OrderByDescending(x => x.LogDate).ToList(),
            Checklists = checklists.Models.Where(x => taskIds.Contains(x.TaskId)).OrderBy(x => x.PositionIndex).ToList(),
            Activity = activity.Models.OrderByDescending(x => x.CreatedAt).Take(100).ToList()
            ,Assignments = assignments.Models.Where(x => taskIds.Contains(x.TaskId)).ToList()
            ,Dependencies = dependencies.Models.Where(x => taskIds.Contains(x.TaskId)).ToList()
        };
    }

    public async Task<bool> UpdateBoardAsync(
        Guid boardId,
        string name,
        string? description,
        string status,
        string health = "on_track",
        DateTime? plannedStart = null,
        DateTime? plannedEnd = null,
        decimal? budgetAmount = null)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<Board>()
            .Where(b => b.Id == boardId)
            .Set(b => b.Name, name.Trim())
            .Set(b => b.Description!, description?.Trim())
            .Set(b => b.Status, NormalizeBoardStatus(status))
            .Set(b => b.Health, NormalizeHealth(health))
            .Set(b => b.PlannedStart, plannedStart)
            .Set(b => b.PlannedEnd, plannedEnd)
            .Set(b => b.BudgetAmount, budgetAmount.HasValue ? Math.Max(0, budgetAmount.Value) : null)
            .Update();
        return response.Models.Count > 0;
    }

    public async Task<bool> DeleteBoardAsync(Guid boardId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<Board>().Where(b => b.Id == boardId).Delete();
        return true;
    }

    public async Task<PulseTask?> CreateTaskAsync(
        PulseTask task,
        IEnumerable<Guid>? collaboratorIds = null,
        bool anonymous = false)
    {
        var client = anonymous
            ? _clientFactory.CreateAnonymousClient()
            : await _clientFactory.CreateForCurrentUserAsync();

        task.Title = task.Title.Trim();
        task.Description = task.Description?.Trim();
        task.Priority = NormalizePriority(task.Priority);
        task.Status = string.IsNullOrWhiteSpace(task.Status) ? "todo" : task.Status;
        task.TargetMonth = string.IsNullOrWhiteSpace(task.TargetMonth) ? null : task.TargetMonth.Trim();
        task.BlockerReason = task.IsBlocked ? task.BlockerReason?.Trim() : null;
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        task.CompletedAt = task.Status == "done" ? DateTime.UtcNow : null;

        if (!anonymous)
        {
            await EnsureBoardAndStatusAsync(client, task.BoardId, task.Status);
            var existingTasks = await client.From<PulseTask>()
                .Where(existing => existing.BoardId == task.BoardId)
                .Get();
            task.PositionIndex = existingTasks.Models
                .Where(existing => existing.Status == task.Status)
                .Select(existing => existing.PositionIndex)
                .DefaultIfEmpty(-1)
                .Max() + 1;
        }

        var response = await client.From<PulseTask>().Insert(task);
        var created = response.Models.FirstOrDefault();
        if (created == null)
        {
            return created;
        }
        if (anonymous && collaboratorIds == null)
        {
            return created;
        }

        try
        {
            await ReplaceCollaboratorsAsync(client, created.Id, collaboratorIds ?? []);
        }
        catch
        {
            await client.From<PulseTask>().Where(existing => existing.Id == created.Id).Delete();
            throw;
        }

        return created;
    }

    public async Task<PulseTask?> UpdateTaskAsync(
        PulseTask task,
        IEnumerable<Guid>? collaboratorIds = null)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var existingResponse = await client.From<PulseTask>()
            .Where(existing => existing.Id == task.Id)
            .Get();
        var existingTask = existingResponse.Models.FirstOrDefault();
        if (existingTask == null || existingTask.BoardId != task.BoardId)
        {
            throw new InvalidOperationException("Tarefa não encontrada neste quadro.");
        }

        await EnsureBoardAndStatusAsync(client, task.BoardId, task.Status);
        var currentCollaborators = await client.From<TaskCollaborator>()
            .Where(collaborator => collaborator.TaskId == task.Id)
            .Get();

        DateTime? completedAt = task.Status == "done"
            ? existingTask.CompletedAt ?? DateTime.UtcNow
            : null;
        var response = await client.From<PulseTask>()
            .Where(t => t.Id == task.Id)
            .Set(t => t.Title, task.Title.Trim())
            .Set(t => t.Description!, task.Description?.Trim())
            .Set(t => t.Status, task.Status)
            .Set(t => t.Priority, NormalizePriority(task.Priority))
            .Set(t => t.StartDate, task.StartDate)
            .Set(t => t.DueDate, task.DueDate)
            .Set(t => t.CompletedAt, completedAt)
            .Set(t => t.AssignedTo, task.AssignedTo)
            .Set(t => t.TargetMonth!, task.TargetMonth)
            .Set(t => t.IsBlocked, task.IsBlocked)
            .Set(t => t.BlockerReason!, task.IsBlocked ? task.BlockerReason?.Trim() : null)
            .Set(t => t.ClientId, task.ClientId)
            .Set(t => t.EstimatedMinutes, Math.Max(0, task.EstimatedMinutes))
            .Set(t => t.UpdatedAt, DateTime.UtcNow)
            .Update();

        if (response.Models.Count == 0)
        {
            return null;
        }

        try
        {
            await ReplaceCollaboratorsAsync(client, task.Id, collaboratorIds ?? []);
        }
        catch
        {
            await ReplaceCollaboratorsAsync(
                client,
                task.Id,
                currentCollaborators.Models.Select(collaborator => collaborator.UserId));
            throw;
        }

        return response.Models.FirstOrDefault();
    }

    public async Task<bool> MoveTaskAsync(Guid taskId, string newStatus, int positionIndex)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var currentResponse = await client.From<PulseTask>().Where(t => t.Id == taskId).Get();
        var current = currentResponse.Models.FirstOrDefault();
        if (current == null) return false;
        var response = await client.From<PulseTask>()
            .Where(t => t.Id == taskId)
            .Set(t => t.Status, newStatus)
            .Set(t => t.PositionIndex, Math.Max(0, positionIndex))
            .Set(t => t.CompletedAt, newStatus == "done" ? current.CompletedAt ?? DateTime.UtcNow : null)
            .Set(t => t.WorkflowState, newStatus == "done" ? "done" : current.WorkflowState == "done" ? "in_progress" : current.WorkflowState)
            .Update();
        return response.Models.Count > 0;
    }

    public async Task<bool> UpdateTaskScheduleAsync(Guid taskId, DateTime startDate, DateTime dueDate)
    {
        if (taskId == Guid.Empty) throw new InvalidOperationException("Tarefa inválida.");
        startDate = startDate.Date;
        dueDate = dueDate.Date;
        if (dueDate < startDate)
            throw new InvalidOperationException("O prazo não pode ser anterior à data de início.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<PulseTask>()
            .Where(task => task.Id == taskId)
            .Set(task => task.StartDate, startDate)
            .Set(task => task.DueDate, dueDate)
            .Set(task => task.UpdatedAt, DateTime.UtcNow)
            .Update();
        return response.Models.Count > 0;
    }

    public async Task<bool> DeleteTaskAsync(Guid taskId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<PulseTask>().Where(t => t.Id == taskId).Delete();
        return true;
    }

    public async Task<TaskComment?> AddCommentAsync(
        Guid taskId,
        Guid userId,
        string? content,
        IReadOnlyList<CommentImageUpload>? images = null,
        string messageType = "message")
    {
        images ??= [];
        var normalizedContent = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedContent) && images.Count == 0)
            throw new InvalidOperationException("Escreva uma mensagem ou anexe uma imagem.");
        if (normalizedContent.Length > 5000)
            throw new InvalidOperationException("A mensagem deve ter no máximo 5.000 caracteres.");
        if (messageType is not ("message" or "question"))
            throw new InvalidOperationException("Tipo de mensagem inválido.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        var task = await client.From<PulseTask>().Where(t => t.Id == taskId).Single()
            ?? throw new InvalidOperationException("Tarefa não encontrada ou sem acesso.");
        var response = await client.From<TaskComment>().Insert(new TaskComment
        {
            TaskId = taskId,
            UserId = userId,
            Content = string.IsNullOrWhiteSpace(normalizedContent) ? "Imagem anexada" : normalizedContent,
            MessageType = messageType,
            CreatedAt = DateTime.UtcNow
        });
        var comment = response.Models.FirstOrDefault();
        if (comment == null || images.Count == 0) return comment;

        var storage = _clientFactory.CreateServiceClient().Storage.From(TaskChatBucket);
        var uploadedPaths = new List<string>();
        try
        {
            foreach (var image in images)
            {
                var extension = ImageExtension(image.ContentType);
                var storagePath = $"{task.BoardId:N}/{taskId:N}/{comment.Id:N}/{Guid.NewGuid():N}{extension}";
                await storage.Upload(image.Content, storagePath, new Supabase.Storage.FileOptions
                {
                    CacheControl = "3600",
                    ContentType = image.ContentType,
                    Upsert = false
                });
                uploadedPaths.Add(storagePath);
                await client.From<TaskCommentAttachment>().Insert(new TaskCommentAttachment
                {
                    CommentId = comment.Id,
                    TaskId = taskId,
                    UploadedBy = userId,
                    StoragePath = storagePath,
                    FileName = image.FileName,
                    ContentType = image.ContentType,
                    FileSize = image.Content.LongLength,
                    CreatedAt = DateTime.UtcNow
                });
            }
            return comment;
        }
        catch (Exception exception)
        {
            if (uploadedPaths.Count > 0)
            {
                try { await storage.Remove(uploadedPaths); }
                catch (Exception cleanupError) { _logger.LogWarning(cleanupError, "Falha ao limpar anexos do comentário {CommentId}", comment.Id); }
            }
            await client.From<TaskComment>().Where(c => c.Id == comment.Id && c.UserId == userId).Delete();
            if (exception is Postgrest.Exceptions.PostgrestException postgrestException && IsMissingChatAttachmentTable(postgrestException))
                throw new InvalidOperationException("O módulo de imagens do chat ainda não foi instalado no Supabase.", exception);
            throw;
        }
    }

    public async Task<bool> UpdateCommentAsync(Guid commentId, Guid userId, string content)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<TaskComment>()
            .Where(c => c.Id == commentId && c.UserId == userId && c.DeletedAt == null)
            .Set(c => c.Content, content.Trim())
            .Set(c => c.UpdatedAt, DateTime.UtcNow)
            .Update();
        return response.Models.Count > 0;
    }

    public async Task<bool> DeleteCommentAsync(Guid commentId, Guid userId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var comment = await client.From<TaskComment>()
            .Where(c => c.Id == commentId && c.UserId == userId)
            .Single();
        if (comment == null || comment.DeletedAt.HasValue) return false;

        var attachments = await client.From<TaskCommentAttachment>()
            .Where(a => a.CommentId == commentId)
            .Get();
        var response = await client.From<TaskComment>()
            .Where(c => c.Id == commentId && c.UserId == userId && c.DeletedAt == null)
            .Set(c => c.Content, "Mensagem excluída")
            .Set(c => c.DeletedAt, DateTime.UtcNow)
            .Set(c => c.UpdatedAt, DateTime.UtcNow)
            .Update();
        if (response.Models.Count == 0) return false;

        foreach (var attachment in attachments.Models)
            await client.From<TaskCommentAttachment>().Where(a => a.Id == attachment.Id).Delete();
        if (attachments.Models.Count > 0)
        {
            try
            {
                await _clientFactory.CreateServiceClient().Storage.From(TaskChatBucket)
                    .Remove(attachments.Models.Select(a => a.StoragePath).ToList());
            }
            catch (Exception cleanupError)
            {
                _logger.LogWarning(cleanupError, "Falha ao remover arquivos do comentário {CommentId}", commentId);
            }
        }
        return true;
    }

    public async Task<CommentAttachmentContent?> GetCommentAttachmentAsync(Guid attachmentId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var attachment = await client.From<TaskCommentAttachment>()
            .Where(a => a.Id == attachmentId)
            .Single();
        if (attachment == null) return null;
        var bytes = await _clientFactory.CreateServiceClient().Storage.From(TaskChatBucket)
            .Download(attachment.StoragePath, (EventHandler<float>?)null);
        return new CommentAttachmentContent(attachment.FileName, attachment.ContentType, bytes);
    }

    private static string ImageExtension(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => throw new InvalidOperationException("Formato de imagem não permitido.")
    };

    private static bool IsMissingChatAttachmentTable(Postgrest.Exceptions.PostgrestException exception) =>
        exception.Content?.Contains("\"code\":\"PGRST205\"", StringComparison.OrdinalIgnoreCase) == true &&
        exception.Content.Contains("task_comment_attachments", StringComparison.OrdinalIgnoreCase);

    public async Task<TimeLog?> AddTimeLogAsync(TimeLog log)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var taskResponse = await client.From<PulseTask>().Where(t => t.Id == log.TaskId).Get();
        var task = taskResponse.Models.FirstOrDefault()
            ?? throw new InvalidOperationException("Tarefa não encontrada.");
        var rates = await client.From<UserRate>().Where(rate => rate.UserId == log.UserId).Get();
        var contracts = await client.From<ClientContract>().Get();
        var contract = contracts.Models.FirstOrDefault(item => item.IsActive &&
            task.ClientId.HasValue && item.ClientId == task.ClientId.Value &&
            (!item.BoardId.HasValue || item.BoardId == task.BoardId) &&
            item.StartsOn.Date <= log.LogDate.Date && (!item.EndsOn.HasValue || item.EndsOn.Value.Date >= log.LogDate.Date));
        log.Minutes = Math.Max(1, log.Minutes);
        log.LogDate = log.LogDate == default ? DateTime.UtcNow.Date : log.LogDate;
        log.CostRateSnapshot = rates.Models.FirstOrDefault()?.HourlyRate ?? 0;
        log.BillingRateSnapshot = log.IsBillable ? contract?.BillingRate ?? 0 : 0;
        log.ApprovalStatus = "pending";
        log.BillingStatus = "unbilled";
        log.CreatedAt = DateTime.UtcNow;
        var response = await client.From<TimeLog>().Insert(log);
        return response.Models.FirstOrDefault();
    }

    public async Task<TaskChecklist?> AddChecklistItemAsync(Guid taskId, string title, int position)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<TaskChecklist>().Insert(new TaskChecklist
        {
            TaskId = taskId,
            Title = title.Trim(),
            PositionIndex = position,
            IsCompleted = false,
            CreatedAt = DateTime.UtcNow
        });
        return response.Models.FirstOrDefault();
    }

    public async Task<bool> ToggleChecklistItemAsync(Guid id, bool completed)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<TaskChecklist>()
            .Where(i => i.Id == id)
            .Set(i => i.IsCompleted, completed)
            .Update();
        return response.Models.Count > 0;
    }

    public async Task<bool> DeleteChecklistItemAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TaskChecklist>().Where(i => i.Id == id).Delete();
        return true;
    }

    private static async Task EnsureBoardAndStatusAsync(
        Supabase.Client client,
        Guid boardId,
        string status)
    {
        var response = await client.From<Board>()
            .Where(board => board.Id == boardId)
            .Get();
        var board = response.Models.FirstOrDefault();
        if (board == null)
        {
            throw new InvalidOperationException("Quadro não encontrado ou sem permissão de acesso.");
        }

        var validStatuses = board.Settings?.Count > 0
            ? board.Settings.Select(column => column.Id)
            : ["todo", "in-progress", "done"];
        if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A etapa selecionada não existe neste quadro.");
        }
    }

    private static async Task ReplaceCollaboratorsAsync(
        Supabase.Client client,
        Guid taskId,
        IEnumerable<Guid> collaboratorIds)
    {
        await client.From<TaskCollaborator>()
            .Where(collaborator => collaborator.TaskId == taskId)
            .Delete();

        foreach (var userId in collaboratorIds.Where(id => id != Guid.Empty).Distinct())
        {
            await client.From<TaskCollaborator>().Insert(new TaskCollaborator
            {
                TaskId = taskId,
                UserId = userId,
                Role = "collaborator",
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    private static string NormalizePriority(string? priority)
    {
        return priority?.Trim().ToLowerInvariant() switch
        {
            "low" or "baixa" or "baixo" => "low",
            "high" or "alta" or "alto" => "high",
            "critical" or "crítica" or "critica" or "urgente" => "critical",
            _ => "medium"
        };
    }

    private static string NormalizeBoardStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "paused" => "paused",
            "archived" => "archived",
            _ => "active"
        };
    }

    private static string NormalizeHealth(string? health) => health switch
    {
        "at_risk" => "at_risk",
        "off_track" => "off_track",
        "on_hold" => "on_hold",
        _ => "on_track"
    };
}
#pragma warning restore CS8603
