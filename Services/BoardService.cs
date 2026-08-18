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
        ValidateBoardInput(name, plannedStart, plannedEnd, budgetAmount);
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

    public async Task<PulseTask?> CreateSubtaskAsync(Guid parentTaskId, Guid creatorId, string title,
        Guid? assignedTo, DateTime? dueDate, int estimatedMinutes)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var parent = await client.From<PulseTask>().Where(x => x.Id == parentTaskId).Single()
            ?? throw new InvalidOperationException("Tarefa principal não encontrada.");
        return await CreateTaskAsync(new PulseTask
        {
            BoardId = parent.BoardId, ParentTaskId = parentTaskId, Title = title.Trim(),
            Status = parent.Status == "done" ? "todo" : parent.Status, Priority = parent.Priority,
            AssignedTo = assignedTo, AccountableOwnerId = parent.AccountableOwnerId ?? creatorId,
            CreatedBy = creatorId, WorkflowState = assignedTo.HasValue ? "inbox" : "waiting_external",
            DueDate = dueDate, ClientId = parent.ClientId, TargetMonth = parent.TargetMonth,
            EstimatedMinutes = Math.Max(0, estimatedMinutes)
        });
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
        return response.Models.Where(t => t.ArchivedAt == null)
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
        // postgrest-csharp only accepts binary comparisons in expression trees.
        // A bare boolean member (p => p.IsActive) cannot be translated to a REST filter.
        var profiles = await client.From<Profile>().Where(p => p.IsActive == true).Get();
        var clients = await client.From<ClientAccount>().Get();
        var activeTasks = tasks.Models.Where(t => t.ArchivedAt == null).ToList();
        var taskIds = activeTasks.Select(t => t.Id).ToHashSet();

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
        var files = await client.From<TaskFile>().Get();
        var approvalSteps = await client.From<TaskApprovalStep>().Get();
        var approvalDelegations = await client.From<ApprovalDelegation>().Get();
        var templates = await client.From<TaskTemplate>().Where(x => x.IsActive == true).Get();
        var ownerTeamId = profiles.Models.FirstOrDefault(x => x.Id == board.OwnerId)?.TeamId;

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
            Tasks = activeTasks.OrderBy(t => t.PositionIndex).ThenBy(t => t.CreatedAt).ToList(),
            ArchivedTasks = tasks.Models.Where(t => t.ArchivedAt != null).OrderByDescending(t => t.ArchivedAt).ToList(),
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
            ,Files = files.Models.Where(x => taskIds.Contains(x.TaskId)).OrderByDescending(x => x.CreatedAt).ToList()
            ,ApprovalSteps = approvalSteps.Models.Where(x => taskIds.Contains(x.TaskId)).OrderBy(x => x.Sequence).ToList()
            ,ApprovalDelegations = approvalDelegations.Models.ToList()
            ,TaskTemplates = templates.Models.Where(x =>
                (!x.BoardId.HasValue || x.BoardId == boardId) &&
                (!x.TeamId.HasValue || x.TeamId == ownerTeamId)).OrderBy(x => x.Name).ToList()
        };
    }

    public async Task DecideApprovalAsync(Guid stepId, string decision, string? note) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).Rpc("decide_task_approval",
            new { p_step_id = stepId, p_decision = decision, p_note = note });

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
        ValidateBoardInput(name, plannedStart, plannedEnd, budgetAmount);
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
        var response = await client.From<Board>().Where(b => b.Id == boardId)
            .Set(b => b.Status, "archived").Update();
        return response.Models.Count > 0;
    }

    public async Task<bool> RestoreBoardAsync(Guid boardId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<Board>().Where(b => b.Id == boardId)
            .Set(b => b.Status, "active").Update();
        return response.Models.Count > 0;
    }

    public async Task<PulseTask?> CreateTaskAsync(
        PulseTask task,
        IEnumerable<Guid>? collaboratorIds = null,
        bool anonymous = false)
    {
        if (anonymous) throw new InvalidOperationException("O cadastro público de tarefas está desativado por segurança.");
        var client = await _clientFactory.CreateForCurrentUserAsync();

        task.Title = task.Title.Trim();
        task.Description = task.Description?.Trim();
        task.Priority = NormalizePriority(task.Priority);
        task.Status = string.IsNullOrWhiteSpace(task.Status) ? "todo" : task.Status;
        task.TargetMonth = string.IsNullOrWhiteSpace(task.TargetMonth) ? null : task.TargetMonth.Trim();
        task.BlockerReason = task.IsBlocked ? task.BlockerReason?.Trim() : null;
        task.CreatedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;
        task.CompletedAt = task.Status == "done" ? DateTime.UtcNow : null;

        await EnsureBoardAndStatusAsync(client, task.BoardId, task.Status);
        task.Id = task.Id == Guid.Empty ? Guid.NewGuid() : task.Id;
        await client.Rpc("create_task_atomic", new
        {
            p_task_id = task.Id, p_board_id = task.BoardId, p_title = task.Title,
            p_description = task.Description, p_status = task.Status, p_priority = task.Priority,
            p_start_date = task.StartDate, p_due_date = task.DueDate, p_assigned_to = task.AssignedTo,
            p_accountable_owner_id = task.AccountableOwnerId, p_created_by = task.CreatedBy,
            p_workflow_state = task.WorkflowState, p_client_id = task.ClientId, p_target_month = task.TargetMonth,
            p_estimated_minutes = task.EstimatedMinutes, p_parent_task_id = task.ParentTaskId,
            p_collaborator_ids = (collaboratorIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray()
        });
        return await client.From<PulseTask>().Where(existing => existing.Id == task.Id).Single();
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
        await client.Rpc("update_task_atomic", new
        {
            p_task_id = task.Id, p_expected_version = task.RowVersion,
            p_title = task.Title.Trim(), p_description = task.Description?.Trim(),
            p_status = task.Status, p_priority = NormalizePriority(task.Priority), p_start_date = task.StartDate,
            p_due_date = task.DueDate, p_assigned_to = task.AssignedTo, p_client_id = task.ClientId,
            p_target_month = string.IsNullOrWhiteSpace(task.TargetMonth) ? null : task.TargetMonth.Trim(),
            p_estimated_minutes = Math.Max(0, task.EstimatedMinutes), p_sla_minutes = task.SlaMinutes,
            p_planned_value = task.PlannedValue, p_custom_fields = task.CustomFields,
            p_is_blocked = task.IsBlocked, p_blocker_reason = task.BlockerReason,
            p_collaborator_ids = (collaboratorIds ?? []).Where(id => id != Guid.Empty).Distinct().ToArray()
        });
        return await client.From<PulseTask>().Where(existing => existing.Id == task.Id).Single();
    }

    public async Task<bool> MoveTaskAsync(Guid taskId, string newStatus, int positionIndex)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("move_task_atomic", new
        {
            p_task_id = taskId, p_new_status = newStatus.Trim(), p_position = Math.Max(0, positionIndex)
        });
        return true;
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
        await client.Rpc("archive_task", new { p_task_id = taskId });
        return true;
    }

    public async Task<bool> RestoreTaskAsync(Guid taskId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("restore_task", new { p_task_id = taskId });
        return true;
    }

    public async Task<TaskComment?> AddCommentAsync(
        Guid taskId,
        Guid userId,
        string? content,
        IReadOnlyList<CommentImageUpload>? images = null,
        string messageType = "message",
        Guid? replyToId = null,
        IReadOnlyList<Guid>? mentionedUserIds = null)
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
        if (replyToId.HasValue)
        {
            var replied = await client.From<TaskComment>().Where(c => c.Id == replyToId.Value).Single();
            if (replied == null || replied.TaskId != taskId || replied.DeletedAt.HasValue)
                throw new InvalidOperationException("A mensagem respondida não pertence a esta tarefa.");
        }
        var response = await client.From<TaskComment>().Insert(new TaskComment
        {
            TaskId = taskId,
            UserId = userId,
            Content = string.IsNullOrWhiteSpace(normalizedContent) ? "Imagem anexada" : normalizedContent,
            MessageType = messageType,
            ReplyToId = replyToId,
            CreatedAt = DateTime.UtcNow
        });
        var comment = response.Models.FirstOrDefault();
        if (comment == null) return null;
        foreach (var mentionedUserId in (mentionedUserIds ?? []).Where(id => id != Guid.Empty && id != userId).Distinct())
        {
            await client.From<TaskMention>().Insert(new TaskMention
            {
                TaskId = taskId, CommentId = comment.Id, MentionedUserId = mentionedUserId,
                MentionedBy = userId, CreatedAt = DateTime.UtcNow
            });
        }
        if (images.Count == 0) return comment;

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

    public async Task<TaskFile?> AddTaskFileAsync(Guid taskId, Guid userId, string fileName, string contentType,
        byte[] content, string? description, Guid? previousVersionId = null)
    {
        if (content.Length == 0 || content.Length > 25 * 1024 * 1024)
            throw new InvalidOperationException("O arquivo deve ter no máximo 25 MB.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var task = await client.From<PulseTask>().Where(x => x.Id == taskId).Single()
            ?? throw new InvalidOperationException("Tarefa não encontrada.");
        var version = 1;
        if (previousVersionId.HasValue)
        {
            var previous = await client.From<TaskFile>().Where(x => x.Id == previousVersionId.Value).Single();
            if (previous == null || previous.TaskId != taskId) throw new InvalidOperationException("Versão anterior inválida.");
            version = previous.Version + 1;
        }
        var safeExtension = Path.GetExtension(fileName).ToLowerInvariant();
        if (safeExtension.Length > 10) safeExtension = string.Empty;
        var storagePath = $"files/{task.BoardId:N}/{taskId:N}/{Guid.NewGuid():N}{safeExtension}";
        await _clientFactory.CreateServiceClient().Storage.From(TaskChatBucket).Upload(content, storagePath,
            new Supabase.Storage.FileOptions { CacheControl = "3600", ContentType = contentType, Upsert = false });
        try
        {
            var inserted = await client.From<TaskFile>().Insert(new TaskFile
            {
                TaskId = taskId, UploadedBy = userId, StoragePath = storagePath, FileName = Path.GetFileName(fileName),
                ContentType = contentType, FileSize = content.LongLength, Description = description?.Trim(),
                Version = version, PreviousVersionId = previousVersionId, CreatedAt = DateTime.UtcNow
            });
            return inserted.Models.FirstOrDefault();
        }
        catch
        {
            await _clientFactory.CreateServiceClient().Storage.From(TaskChatBucket).Remove([storagePath]);
            throw;
        }
    }

    public async Task<CommentAttachmentContent?> GetTaskFileAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var file = await client.From<TaskFile>().Where(x => x.Id == id).Single();
        if (file == null) return null;
        var bytes = await _clientFactory.CreateServiceClient().Storage.From(TaskChatBucket)
            .Download(file.StoragePath, (EventHandler<float>?)null);
        return new CommentAttachmentContent(file.FileName, file.ContentType, bytes);
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

    private static void ValidateBoardInput(string? name, DateTime? plannedStart, DateTime? plannedEnd, decimal? budgetAmount)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
            throw new InvalidOperationException("O nome do quadro deve ter entre 1 e 120 caracteres.");
        if (plannedStart.HasValue && plannedEnd.HasValue && plannedEnd.Value.Date < plannedStart.Value.Date)
            throw new InvalidOperationException("O fim planejado não pode ser anterior ao início.");
        if (budgetAmount < 0) throw new InvalidOperationException("O orçamento não pode ser negativo.");
    }
}
#pragma warning restore CS8603
