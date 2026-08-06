using PulseBoardMigration.Models;

#pragma warning disable CS8603 // Postgrest Set<T?> expression trees report nullable false positives.

namespace PulseBoardMigration.Services;

public class WorkManagementService
{
    private readonly SupabaseClientFactory _clientFactory;

    public WorkManagementService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<MyWorkViewModel> GetMyWorkAsync(Guid userId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var tasks = await client.From<PulseTask>().Get();
        var boards = await client.From<Board>().Get();
        var profiles = await client.From<Profile>().Get();
        var assignments = await client.From<TaskAssignment>().Get();
        var followers = await client.From<TaskFollower>().Where(x => x.UserId == userId).Get();

        return new MyWorkViewModel
        {
            CurrentUserId = userId,
            Tasks = tasks.Models.OrderBy(x => x.DueDate ?? DateTime.MaxValue).ToList(),
            Boards = boards.Models.ToList(),
            Profiles = profiles.Models.ToList(),
            Assignments = assignments.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            Followers = followers.Models.ToList()
        };
    }

    public async Task<List<UserNotification>> GetNotificationsAsync(Guid userId, int take = 40)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("ensure_due_notifications", new { });
        var response = await client.From<UserNotification>()
            .Where(x => x.RecipientId == userId)
            .Get();
        return response.Models
            .Where(x => x.ArchivedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToList();
    }

    public async Task MarkNotificationsReadAsync(Guid userId, Guid? notificationId = null)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var unread = await client.From<UserNotification>()
            .Where(x => x.RecipientId == userId)
            .Get();
        foreach (var item in unread.Models.Where(x => x.ReadAt == null && (!notificationId.HasValue || x.Id == notificationId)))
        {
            await client.From<UserNotification>()
                .Where(x => x.Id == item.Id)
                .Set(x => x.ReadAt, DateTime.UtcNow)
                .Update();
        }
    }

    public async Task HandoffAsync(
        Guid taskId,
        Guid toUserId,
        string stage,
        DateTime? dueDate,
        int estimatedMinutes,
        string? notes,
        string? acceptanceCriteria,
        bool requiresAcceptance,
        Guid? acceptanceBy)
    {
        if (taskId == Guid.Empty || toUserId == Guid.Empty || string.IsNullOrWhiteSpace(stage))
        {
            throw new InvalidOperationException("Informe a etapa e o novo executor.");
        }

        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("handoff_task", new
        {
            p_task_id = taskId,
            p_to_user_id = toUserId,
            p_stage = stage.Trim(),
            p_due_date = dueDate,
            p_estimated_minutes = Math.Max(0, estimatedMinutes),
            p_notes = notes?.Trim(),
            p_acceptance_criteria = acceptanceCriteria?.Trim(),
            p_requires_acceptance = requiresAcceptance,
            p_acceptance_by = requiresAcceptance ? acceptanceBy : null
        });
    }

    public async Task RespondAssignmentAsync(Guid assignmentId, string action, string? note)
    {
        if (action is not ("accept" or "reject" or "complete"))
        {
            throw new InvalidOperationException("Ação inválida.");
        }
        if (action == "reject" && string.IsNullOrWhiteSpace(note))
        {
            throw new InvalidOperationException("Informe o motivo da recusa.");
        }

        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("respond_task_assignment", new
        {
            p_assignment_id = assignmentId,
            p_action = action,
            p_note = note?.Trim()
        });
    }

    public async Task ReturnWithQuestionAsync(Guid taskId, Guid toUserId, string question)
    {
        if (taskId == Guid.Empty || toUserId == Guid.Empty || string.IsNullOrWhiteSpace(question))
            throw new InvalidOperationException("Informe a pessoa e descreva a dúvida.");
        if (question.Trim().Length > 5000)
            throw new InvalidOperationException("A dúvida deve ter no máximo 5.000 caracteres.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("return_task_with_question", new
        {
            p_task_id = taskId,
            p_to_user_id = toUserId,
            p_question = question.Trim()
        });
    }

    public async Task ReviewTaskAsync(Guid taskId, string action, string? note)
    {
        if (action is not ("approve" or "changes"))
        {
            throw new InvalidOperationException("Ação de revisão inválida.");
        }
        if (action == "changes" && string.IsNullOrWhiteSpace(note))
        {
            throw new InvalidOperationException("Descreva os ajustes solicitados.");
        }

        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("review_task", new
        {
            p_task_id = taskId,
            p_action = action,
            p_note = note?.Trim()
        });
    }

    public async Task<ManagementViewModel?> GetManagementAsync(Guid userId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var currentResponse = await client.From<Profile>().Where(x => x.Id == userId).Get();
        var current = currentResponse.Models.FirstOrDefault();
        if (current?.Role is not ("manager" or "admin")) return null;

        var profiles = await client.From<Profile>().Get();
        var teams = await client.From<Team>().Get();
        var boards = await client.From<Board>().Get();
        var tasks = await client.From<PulseTask>().Get();
        var schedules = await client.From<WorkSchedule>().Get();
        var assignments = await client.From<TaskAssignment>().Get();
        var visibleProfiles = current.Role == "admin"
            ? profiles.Models
            : profiles.Models.Where(x => x.TeamId == current.TeamId).ToList();
        var visibleIds = visibleProfiles.Select(x => x.Id).ToHashSet();

        return new ManagementViewModel
        {
            CurrentUser = current,
            Profiles = visibleProfiles.OrderBy(x => x.FullName ?? x.Email).ToList(),
            Teams = teams.Models.ToList(),
            Boards = boards.Models.ToList(),
            Tasks = tasks.Models.Where(x => x.AssignedTo.HasValue && visibleIds.Contains(x.AssignedTo.Value)).ToList(),
            Schedules = schedules.Models.ToList(),
            Assignments = assignments.Models.ToList()
        };
    }

    public async Task SaveWorkScheduleAsync(Guid userId, int weeklyCapacityMinutes)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<WorkSchedule>()
            .Where(x => x.UserId == userId)
            .Get();
        var current = response.Models.FirstOrDefault(x => x.ValidTo == null);
        if (current == null)
        {
            await client.From<WorkSchedule>().Insert(new WorkSchedule
            {
                UserId = userId,
                WeeklyCapacityMinutes = Math.Clamp(weeklyCapacityMinutes, 0, 10080),
                ValidFrom = DateTime.UtcNow.Date,
                CreatedAt = DateTime.UtcNow
            });
            return;
        }

        await client.From<WorkSchedule>()
            .Where(x => x.Id == current.Id)
            .Set(x => x.WeeklyCapacityMinutes, Math.Clamp(weeklyCapacityMinutes, 0, 10080))
            .Update();
    }

    public async Task<CompanyScheduleViewModel> GetCompanyScheduleAsync(DateTime? from, DateTime? to)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var rangeStart = (from ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
        var rangeEnd = (to ?? rangeStart.AddDays(90)).Date;
        if (rangeEnd <= rangeStart) rangeEnd = rangeStart.AddDays(30);
        var totalDays = Math.Max(1, (rangeEnd - rangeStart).TotalDays);
        var tasks = await client.From<PulseTask>().Get();
        var boards = await client.From<Board>().Get();
        var profiles = await client.From<Profile>().Get();
        var milestones = await client.From<ProjectMilestone>().Get();

        var rows = tasks.Models
            .Where(x => x.StartDate.HasValue || x.DueDate.HasValue)
            .Select(task =>
            {
                var start = (task.StartDate ?? task.DueDate ?? rangeStart).Date;
                var end = (task.DueDate ?? task.StartDate ?? start).Date;
                if (end < start) end = start;
                var clippedStart = start < rangeStart ? rangeStart : start;
                var clippedEnd = end > rangeEnd ? rangeEnd : end;
                return new ScheduleRow
                {
                    TaskId = task.Id,
                    BoardId = task.BoardId,
                    TaskTitle = task.Title,
                    BoardName = boards.Models.FirstOrDefault(x => x.Id == task.BoardId)?.Name ?? "Projeto",
                    PersonName = profiles.Models.FirstOrDefault(x => x.Id == task.AssignedTo)?.FullName,
                    Status = task.Status,
                    Start = start,
                    End = end,
                    LeftPercent = Math.Clamp((decimal)((clippedStart - rangeStart).TotalDays / totalDays * 100), 0, 100),
                    WidthPercent = Math.Clamp((decimal)(((clippedEnd - clippedStart).TotalDays + 1) / totalDays * 100), 0.7m, 100),
                    IsOverdue = task.Status != "done" && end < DateTime.UtcNow.Date,
                    IsBlocked = task.IsBlocked
                };
            })
            .Where(x => x.End >= rangeStart && x.Start <= rangeEnd)
            .OrderBy(x => x.Start)
            .ToList();

        return new CompanyScheduleViewModel
        {
            From = rangeStart,
            To = rangeEnd,
            Rows = rows,
            Boards = boards.Models.ToList(),
            Milestones = milestones.Models.Where(x => x.DueDate >= rangeStart && x.DueDate <= rangeEnd).ToList()
        };
    }

    public async Task AddMilestoneAsync(Guid boardId, string title, DateTime dueDate)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<ProjectMilestone>().Insert(new ProjectMilestone
        {
            BoardId = boardId,
            Title = title.Trim(),
            DueDate = dueDate.Date,
            Status = "planned",
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task AddDependencyAsync(Guid taskId, Guid dependsOnTaskId)
    {
        if (taskId == Guid.Empty || dependsOnTaskId == Guid.Empty || taskId == dependsOnTaskId)
            throw new InvalidOperationException("Dependência inválida.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var tasks = await client.From<PulseTask>().Get();
        var task = tasks.Models.FirstOrDefault(x => x.Id == taskId);
        var dependency = tasks.Models.FirstOrDefault(x => x.Id == dependsOnTaskId);
        if (task == null || dependency == null || task.BoardId != dependency.BoardId)
            throw new InvalidOperationException("As tarefas precisam pertencer ao mesmo projeto.");
        await client.From<TaskDependency>().Insert(new TaskDependency
        {
            TaskId = taskId,
            DependsOnTaskId = dependsOnTaskId,
            DependencyType = "finish_to_start",
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task DeleteDependencyAsync(Guid dependencyId)
    {
        if (dependencyId == Guid.Empty) throw new InvalidOperationException("Dependência inválida.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TaskDependency>().Where(x => x.Id == dependencyId).Delete();
    }
}
#pragma warning restore CS8603
