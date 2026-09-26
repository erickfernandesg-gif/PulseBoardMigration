using PulseBoardMigration.Models;
using PulseBoardMigration.Domain;

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
            Tasks = tasks.Models.Where(x => x.ArchivedAt == null).OrderBy(x => x.DueDate ?? DateTime.MaxValue).ToList(),
            Boards = boards.Models.ToList(),
            Profiles = profiles.Models.ToList(),
            Assignments = assignments.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            Followers = followers.Models.ToList()
        };
    }

    public async Task<List<UserNotification>> GetNotificationsAsync(Guid userId, int take = 40)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var preference = await GetNotificationPreferenceAsync(userId);
        if (!preference.InApp) return [];
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

    public async Task<NotificationPreference> GetNotificationPreferenceAsync(Guid userId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        return await client.From<NotificationPreference>().Where(x => x.UserId == userId).Single()
            ?? new NotificationPreference { UserId = userId, UpdatedAt = DateTime.UtcNow };
    }

    public async Task SaveNotificationPreferenceAsync(NotificationPreference preference)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var existing = await client.From<NotificationPreference>().Where(x => x.UserId == preference.UserId).Single();
        preference.DigestHour = (short)Math.Clamp((int)preference.DigestHour, 0, 23);
        preference.UpdatedAt = DateTime.UtcNow;
        if (existing == null) { await client.From<NotificationPreference>().Insert(preference); return; }
        await client.From<NotificationPreference>().Where(x => x.UserId == preference.UserId)
            .Set(x => x.InApp, preference.InApp).Set(x => x.EmailDigest, preference.EmailDigest)
            .Set(x => x.DueReminders, preference.DueReminders).Set(x => x.BudgetAlerts, preference.BudgetAlerts)
            .Set(x => x.MentionAlerts, preference.MentionAlerts).Set(x => x.DigestHour, preference.DigestHour)
            .Set(x => x.UpdatedAt, DateTime.UtcNow).Update();
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
        var holidays = await client.From<CompanyHoliday>().Get();
        var absences = await client.From<UserAbsence>().Get();
        var activeTasks = tasks.Models.Where(x => x.ArchivedAt == null).ToList();
        var visibleProfiles = current.Role == "admin"
            ? profiles.Models.Where(x => x.IsActive).ToList()
            : profiles.Models.Where(x => x.IsActive && x.TeamId == current.TeamId).ToList();
        var visibleIds = visibleProfiles.Select(x => x.Id).ToHashSet();

        var today = DateTime.UtcNow.Date;
        var weekEnd = today.AddDays(6);
        var visibleTasks = activeTasks.Where(x => !x.AssignedTo.HasValue || visibleIds.Contains(x.AssignedTo.Value)).ToList();
        var visibleTaskIds = visibleTasks.Select(x => x.Id).ToHashSet();
        var visibleAssignments = assignments.Models.Where(x => visibleTaskIds.Contains(x.TaskId)).ToList();
        var effortTasks = WorkRules.LeafTasksForEffort(visibleTasks);
        var workloads = visibleProfiles.Select(person =>
        {
            var owned = visibleTasks.Where(x => x.AssignedTo == person.Id).ToList();
            var ownedEffort = effortTasks.Where(x => x.AssignedTo == person.Id).ToList();
            var schedule = CurrentScheduleFor(person, schedules.Models, today);
            var planned = ownedEffort
                .Where(x => x.Status != "done" && x.EstimatedMinutes > 0 && (x.StartDate.HasValue || x.DueDate.HasValue))
                .Where(x => (x.StartDate ?? today).Date <= weekEnd && (x.DueDate ?? weekEnd).Date >= today)
                .Sum(x => x.EstimatedMinutes);
            var unplanned = ownedEffort.Count(x => x.Status != "done" &&
                (x.EstimatedMinutes <= 0 || (!x.StartDate.HasValue && !x.DueDate.HasValue)));
            return new ManagementPersonWorkload
            {
                UserId = person.Id,
                Name = person.FullName ?? person.Email,
                Email = person.Email,
                WeeklyCapacityMinutes = schedule?.WeeklyCapacityMinutes ?? 2400,
                EffectiveCapacityMinutes = EffectiveCapacityForPeriod(person, schedules.Models, holidays.Models, absences.Models, today, weekEnd),
                HasConfiguredCapacity = schedule != null,
                PlannedMinutes = planned,
                OpenTasks = owned.Count(x => x.Status != "done"),
                OverdueTasks = owned.Count(x => x.Status != "done" && x.DueDate < today),
                BlockedTasks = owned.Count(x => x.Status != "done" && x.IsBlocked),
                PendingAssignments = visibleAssignments.Count(x => x.ToUserId == person.Id && x.Status == "pending"),
                UnplannedTasks = unplanned
            };
        }).OrderBy(x => x.Name).ToList();

        var openEffortTasks = effortTasks.Where(x => x.Status != "done").ToList();

        return new ManagementViewModel
        {
            CurrentUser = current,
            Profiles = visibleProfiles.OrderBy(x => x.FullName ?? x.Email).ToList(),
            Teams = teams.Models.ToList(),
            Boards = boards.Models.ToList(),
            Tasks = visibleTasks,
            Schedules = schedules.Models.ToList(),
            Assignments = visibleAssignments,
            Holidays = holidays.Models.ToList(),
            Absences = absences.Models.ToList(),
            PeriodStart = today,
            PeriodEnd = weekEnd,
            Workloads = workloads,
            OpenTasksWithoutPlanning = openEffortTasks.Count(x => x.EstimatedMinutes <= 0 || (!x.StartDate.HasValue && !x.DueDate.HasValue)),
            OpenUnassignedTasks = openEffortTasks.Count(x => !x.AssignedTo.HasValue),
            PeopleWithoutConfiguredCapacity = workloads.Count(x => !x.HasConfiguredCapacity),
            OverloadedPeople = workloads.Count(x => x.PlannedMinutes > x.EffectiveCapacityMinutes)
        };
    }

    public async Task SaveWorkScheduleAsync(Guid actorId, Guid userId, int weeklyCapacityMinutes)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var profiles = await client.From<Profile>().Get();
        var actor = profiles.Models.FirstOrDefault(x => x.Id == actorId);
        var target = profiles.Models.FirstOrDefault(x => x.Id == userId);
        if (actor == null || target == null || !target.IsActive ||
            (actor.Role != "admin" && (actor.Role != "manager" || actor.TeamId == null || actor.TeamId != target.TeamId)))
            throw new InvalidOperationException("Você só pode alterar a capacidade de pessoas ativas da sua equipe.");
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

    private static WorkSchedule? CurrentScheduleFor(Profile person, IReadOnlyCollection<WorkSchedule> schedules, DateTime day) =>
        schedules.Where(x => x.UserId == person.Id && x.ValidFrom.Date <= day && (!x.ValidTo.HasValue || x.ValidTo.Value.Date >= day))
            .OrderByDescending(x => x.ValidFrom).FirstOrDefault()
        ?? schedules.Where(x => x.TeamId == person.TeamId && x.ValidFrom.Date <= day && (!x.ValidTo.HasValue || x.ValidTo.Value.Date >= day))
            .OrderByDescending(x => x.ValidFrom).FirstOrDefault();

    private static int EffectiveCapacityForPeriod(Profile person, IReadOnlyCollection<WorkSchedule> schedules,
        IReadOnlyCollection<CompanyHoliday> holidays, IReadOnlyCollection<UserAbsence> absences, DateTime from, DateTime to)
    {
        var total = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var schedule = CurrentScheduleFor(person, schedules, day);
            var workDays = (schedule?.WorkDays ?? "1,2,3,4,5").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
                .Where(value => value is >= 1 and <= 7).ToHashSet();
            if (workDays.Count == 0) workDays = [1, 2, 3, 4, 5];
            var isoDay = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
            if (!workDays.Contains(isoDay) ||
                holidays.Any(x => x.HolidayDate.Date == day && (!x.TeamId.HasValue || x.TeamId == person.TeamId)) ||
                absences.Any(x => x.UserId == person.Id && x.Status == "approved" && x.StartsOn.Date <= day && x.EndsOn.Date >= day))
                continue;
            total += (schedule?.WeeklyCapacityMinutes ?? 2400) / workDays.Count;
        }
        return total;
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
        var portfolioDependencies = await client.From<PortfolioDependency>().Get();
        var dependencies = await client.From<TaskDependency>().Get();
        var criticalIds = boards.Models.SelectMany(board =>
        {
            var projectTasks = tasks.Models.Where(x => x.BoardId == board.Id && x.ArchivedAt == null).ToList();
            var projectIds = projectTasks.Select(x => x.Id).ToHashSet();
            return CriticalPathRules.Calculate(
                projectTasks.Select(x => (x.Id, x.EstimatedMinutes)),
                dependencies.Models.Where(x => projectIds.Contains(x.TaskId) && projectIds.Contains(x.DependsOnTaskId))
                    .Select(x => (x.TaskId, x.DependsOnTaskId)));
        }).ToHashSet();

        var rows = tasks.Models.Where(x => x.ArchivedAt == null)
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
                    AssignedToId = task.AssignedTo,
                    TaskTitle = task.Title,
                    BoardName = boards.Models.FirstOrDefault(x => x.Id == task.BoardId)?.Name ?? "Projeto",
                    PersonName = profiles.Models.FirstOrDefault(x => x.Id == task.AssignedTo)?.FullName,
                    Status = task.Status,
                    Start = start,
                    End = end,
                    LeftPercent = Math.Clamp((decimal)((clippedStart - rangeStart).TotalDays / totalDays * 100), 0, 100),
                    WidthPercent = Math.Clamp((decimal)(((clippedEnd - clippedStart).TotalDays + 1) / totalDays * 100), 0.7m, 100),
                    IsOverdue = task.Status != "done" && end < DateTime.UtcNow.Date,
                    IsBlocked = task.IsBlocked,
                    IsCritical = criticalIds.Contains(task.Id)
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
            Milestones = milestones.Models.Where(x => x.DueDate >= rangeStart && x.DueDate <= rangeEnd).ToList(),
            PortfolioDependencies = portfolioDependencies.Models.ToList()
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
        var task = tasks.Models.FirstOrDefault(x => x.Id == taskId && x.ArchivedAt == null);
        var dependency = tasks.Models.FirstOrDefault(x => x.Id == dependsOnTaskId && x.ArchivedAt == null);
        if (task == null || dependency == null)
            throw new InvalidOperationException("As tarefas precisam existir e estar ativas.");
        if (task.BoardId != dependency.BoardId)
            throw new InvalidOperationException("Use Operações do projeto para criar dependências entre projetos diferentes.");
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
