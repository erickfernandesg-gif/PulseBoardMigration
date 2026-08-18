using PulseBoardMigration.Models;
using PulseBoardMigration.Domain;

namespace PulseBoardMigration.Services;

public class EnterpriseService
{
    private readonly SupabaseClientFactory _clientFactory;

    public EnterpriseService(SupabaseClientFactory clientFactory) => _clientFactory = clientFactory;

    public async Task<EnterprisePlanningViewModel> GetPlanningAsync(Guid currentUserId, bool isAdmin,
        Guid? teamId = null, Guid? boardId = null, DateTime? from = null, DateTime? to = null)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var periodStart = (from ?? DateTime.UtcNow.Date).Date;
        var periodEnd = (to ?? periodStart.AddDays(30)).Date;
        if (periodEnd < periodStart) periodEnd = periodStart.AddDays(30);
        if ((periodEnd - periodStart).TotalDays > 366) periodEnd = periodStart.AddDays(366);
        var boards = await client.From<Board>().Get();
        var profiles = await client.From<Profile>().Get();
        var teams = await client.From<Team>().Get();
        var tasks = await client.From<PulseTask>().Get();
        var schedules = await client.From<WorkSchedule>().Get();
        var holidays = await client.From<CompanyHoliday>().Get();
        var absences = await client.From<UserAbsence>().Get();
        var baselines = await client.From<ProjectBaseline>().Get();
        var dependencies = await client.From<PortfolioDependency>().Get();
        var templates = await client.From<TaskTemplate>().Get();
        var recurring = await client.From<RecurringTaskRule>().Get();
        var currentProfile = profiles.Models.FirstOrDefault(x => x.Id == currentUserId)
            ?? throw new InvalidOperationException("Perfil do usuário não encontrado.");
        var activeProfiles = profiles.Models.Where(x => x.IsActive && (isAdmin || x.TeamId == currentProfile.TeamId))
            .OrderBy(x => x.FullName ?? x.Email).ToList();
        var allowedOwnerIds = activeProfiles.Select(x => x.Id).ToHashSet();
        var activeBoards = boards.Models.Where(x => x.Status != "archived" && (isAdmin || allowedOwnerIds.Contains(x.OwnerId)))
            .OrderBy(x => x.Name).ToList();
        var boardOwners = activeProfiles.ToDictionary(x => x.Id);
        var scopedBoards = activeBoards.Where(board =>
            (!boardId.HasValue || board.Id == boardId.Value) &&
            (!teamId.HasValue || boardOwners.TryGetValue(board.OwnerId, out var owner) && owner.TeamId == teamId)).ToList();
        var scopedBoardIds = scopedBoards.Select(x => x.Id).ToHashSet();
        var scopedProfiles = activeProfiles.Where(x => !teamId.HasValue || x.TeamId == teamId).ToList();
        var scopedTasks = tasks.Models.Where(x => x.ArchivedAt == null && scopedBoardIds.Contains(x.BoardId)).ToList();
        var capacityProfiles = boardId.HasValue && !teamId.HasValue
            ? scopedProfiles.Where(person => scopedTasks.Any(task => task.AssignedTo == person.Id) || scopedBoards.Any(board => board.OwnerId == person.Id)).ToList()
            : scopedProfiles;
        var today = DateTime.UtcNow.Date;
        var relevantHolidays = holidays.Models.Where(x => x.HolidayDate.Date >= periodStart && x.HolidayDate.Date <= periodEnd).ToList();
        var relevantAbsences = absences.Models.Where(x => x.Status == "approved" && x.StartsOn.Date <= periodEnd && x.EndsOn.Date >= periodStart).ToList();
        var capacity = capacityProfiles.Sum(person => CalculateCapacity(
            person, schedules.Models, relevantHolidays, relevantAbsences, periodStart, periodEnd));
        var capacityProfileIds = capacityProfiles.Select(x => x.Id).ToHashSet();
        var allocated = scopedTasks.Where(task => task.Status != "done" && task.AssignedTo.HasValue && capacityProfileIds.Contains(task.AssignedTo.Value) &&
            (task.StartDate ?? periodStart).Date <= periodEnd && (task.DueDate ?? periodEnd).Date >= periodStart)
            .Sum(x => Math.Max(0, x.EstimatedMinutes));
        var dependencyList = dependencies.Models.Where(x => scopedBoardIds.Contains(x.PredecessorBoardId) || scopedBoardIds.Contains(x.SuccessorBoardId)).ToList();
        var conflicts = dependencyList.Where(dependency => IsDependencyConflict(dependency, activeBoards)).ToHashSet();
        var activeBaselines = baselines.Models.Where(x => x.IsActive)
            .GroupBy(x => x.BoardId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Version).First());
        var metrics = scopedBoards.Select(board =>
        {
            var boardTasks = scopedTasks.Where(x => x.BoardId == board.Id).ToList();
            var open = boardTasks.Where(x => x.Status != "done").ToList();
            var forecast = board.ForecastEnd ?? open.Where(x => x.DueDate.HasValue).Select(x => x.DueDate).Max();
            var comparison = forecast ?? board.PlannedEnd;
            var currentEstimate = boardTasks.Sum(x => Math.Max(0, x.EstimatedMinutes));
            var baselineEstimate = activeBaselines.TryGetValue(board.Id, out var baseline)
                ? SnapshotInt(baseline.Snapshot, "estimatedMinutes") : null;
            return new PlanningBoardMetric
            {
                BoardId = board.Id, Name = board.Name, Health = board.Health,
                OpenTasks = open.Count, OverdueTasks = open.Count(x => x.DueDate < today),
                BlockedTasks = open.Count(x => x.IsBlocked), EstimatedMinutes = open.Sum(x => Math.Max(0, x.EstimatedMinutes)),
                BaselineEstimatedMinutes = baselineEstimate,
                EffortVarianceMinutes = baselineEstimate.HasValue ? currentEstimate - baselineEstimate.Value : 0,
                PlannedEnd = board.PlannedEnd, BaselineEnd = board.BaselineEnd, ForecastEnd = forecast,
                ScheduleVarianceDays = board.BaselineEnd.HasValue && comparison.HasValue
                    ? (comparison.Value.Date - board.BaselineEnd.Value.Date).Days : 0,
                HasDependencyConflict = conflicts.Any(x => x.PredecessorBoardId == board.Id || x.SuccessorBoardId == board.Id)
            };
        }).OrderByDescending(x => x.OverdueTasks > 0 || x.BlockedTasks > 0 || x.HasDependencyConflict)
          .ThenByDescending(x => x.ScheduleVarianceDays).ThenBy(x => x.Name).ToList();

        return new EnterprisePlanningViewModel
        {
            Boards = activeBoards,
            Profiles = activeProfiles,
            Teams = teams.Models.Where(x => isAdmin || x.Id == currentProfile.TeamId).OrderBy(x => x.Name).ToList(),
            Holidays = holidays.Models.OrderBy(x => x.HolidayDate < today).ThenBy(x => x.HolidayDate).ToList(),
            Absences = absences.Models.OrderByDescending(x => x.StartsOn).ToList(),
            Baselines = baselines.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            PortfolioDependencies = dependencies.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            Templates = templates.Models.OrderBy(x => x.Name).ToList(),
            RecurringRules = recurring.Models.OrderByDescending(x => x.IsActive).ThenBy(x => x.NextRunAt).ToList(),
            ProjectMetrics = metrics, SelectedTeamId = teamId, SelectedBoardId = boardId,
            CurrentUserTeamId = currentProfile.TeamId, IsAdmin = isAdmin,
            PeriodStart = periodStart, PeriodEnd = periodEnd, EffectiveCapacityMinutes = capacity,
            AllocatedMinutes = allocated, OpenTasks = scopedTasks.Count(x => x.Status != "done"),
            OverdueTasks = scopedTasks.Count(x => x.Status != "done" && x.DueDate < today),
            DependencyConflicts = conflicts.Count
        };
    }

    private static int? SnapshotInt(Dictionary<string, object?> snapshot, string key)
    {
        if (!snapshot.TryGetValue(key, out var value) || value is null) return null;
        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    public async Task AddHolidayAsync(DateTime date, string name, Guid? teamId, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Informe o nome do feriado.");
        if (date == default) throw new InvalidOperationException("Informe a data do feriado.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<CompanyHoliday>().Insert(new CompanyHoliday
        {
            HolidayDate = date.Date, Name = name.Trim(), TeamId = teamId, CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task AddAbsenceAsync(Guid userId, string type, DateTime startsOn, DateTime endsOn, string? notes, Guid actorId)
    {
        if (userId == Guid.Empty) throw new InvalidOperationException("Selecione uma pessoa.");
        if (endsOn.Date < startsOn.Date) throw new InvalidOperationException("O fim da ausência deve ser posterior ao início.");
        if (type is not ("vacation" or "leave" or "training" or "day_off" or "other")) type = "other";
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<UserAbsence>().Insert(new UserAbsence
        {
            UserId = userId, AbsenceType = type, StartsOn = startsOn.Date, EndsOn = endsOn.Date,
            Notes = notes?.Trim(), Status = "approved", CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task CaptureBaselineAsync(Guid boardId, string name, Guid actorId)
    {
        if (boardId == Guid.Empty) throw new InvalidOperationException("Selecione um projeto.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc<Guid>("capture_project_baseline", new
        {
            p_board_id = boardId,
            p_name = string.IsNullOrWhiteSpace(name) ? null : name.Trim()
        });
    }

    public async Task AddPortfolioDependencyAsync(Guid predecessor, Guid successor, string type, int lagDays, Guid actorId)
    {
        if (predecessor == successor) throw new InvalidOperationException("Um projeto não pode depender dele mesmo.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc<Guid>("add_portfolio_dependency", new
        {
            p_predecessor_board_id = predecessor, p_successor_board_id = successor,
            p_dependency_type = PlanningRules.NormalizeDependencyType(type), p_lag_days = Math.Clamp(lagDays, -365, 365)
        });
    }

    public async Task SaveTemplateAsync(string name, string? description, Guid? boardId, Guid? teamId, int estimatedMinutes, string priority, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Informe o nome do modelo.");
        if (name.Trim().Length > 120) throw new InvalidOperationException("O nome do modelo deve ter no máximo 120 caracteres.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        if (boardId.HasValue)
        {
            var board = await client.From<Board>().Where(x => x.Id == boardId.Value).Single()
                ?? throw new InvalidOperationException("Projeto não encontrado.");
            if (teamId.HasValue)
            {
                var owner = await client.From<Profile>().Where(x => x.Id == board.OwnerId).Single();
                if (owner?.TeamId != teamId) throw new InvalidOperationException("A equipe do modelo não corresponde à equipe proprietária do projeto.");
            }
        }
        await client.From<TaskTemplate>().Insert(new TaskTemplate
        {
            Name = name.Trim(), Description = description?.Trim(), BoardId = boardId, TeamId = teamId,
            Definition = new Dictionary<string, object?> { ["estimatedMinutes"] = Math.Clamp(estimatedMinutes, 0, 100000), ["priority"] = PlanningRules.NormalizePriority(priority) },
            IsActive = true, CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task SaveRecurringRuleAsync(Guid boardId, string title, string? description, string cadence,
        int intervalCount, DateTime nextRunAt, string timeZone, string targetStatus, string priority,
        Guid? templateId, Guid? assignedTo, int estimatedMinutes, int dueAfterDays,
        DateTime? endsAt, int? maxOccurrences, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Informe o título da recorrência.");
        if (boardId == Guid.Empty) throw new InvalidOperationException("Selecione um projeto.");
        if (string.IsNullOrWhiteSpace(targetStatus)) throw new InvalidOperationException("Selecione a etapa de destino.");
        timeZone = NormalizeTimeZone(timeZone);
        var utcNextRun = ToUtc(nextRunAt, timeZone);
        DateTime? utcEndsAt = endsAt.HasValue ? ToUtc(endsAt.Value, timeZone) : null;
        if (utcEndsAt.HasValue && utcEndsAt < utcNextRun)
            throw new InvalidOperationException("O término da recorrência não pode ser anterior à primeira execução.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId).Single()
            ?? throw new InvalidOperationException("Projeto não encontrado.");
        if (!board.Settings.Any(x => x.Id == targetStatus))
            throw new InvalidOperationException("A etapa selecionada não existe mais no projeto.");
        await client.From<RecurringTaskRule>().Insert(new RecurringTaskRule
        {
            BoardId = boardId, TemplateId = templateId, Title = title.Trim(), Description = description?.Trim(),
            Cadence = PlanningRules.NormalizeCadence(cadence), IntervalCount = Math.Clamp(intervalCount, 1, 365),
            NextRunAt = utcNextRun, TimeZone = timeZone, TargetStatus = targetStatus,
            Priority = PlanningRules.NormalizePriority(priority), AssignedTo = assignedTo,
            EstimatedMinutes = Math.Clamp(estimatedMinutes, 0, 100000), DueAfterDays = Math.Clamp(dueAfterDays, 0, 365),
            EndsAt = utcEndsAt, MaxOccurrences = maxOccurrences.HasValue ? Math.Clamp(maxOccurrences.Value, 1, 10000) : null,
            IsActive = true, CreatedBy = actorId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
    }

    public async Task DeleteHolidayAsync(Guid id) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).From<CompanyHoliday>().Where(x => x.Id == id).Delete();

    public async Task CancelAbsenceAsync(Guid id)
    {
        if (id == Guid.Empty) throw new InvalidOperationException("Ausência inválida.");
        await (await _clientFactory.CreateForCurrentUserAsync()).From<UserAbsence>().Where(x => x.Id == id)
            .Set(x => x.Status, "cancelled").Update();
    }

    public async Task DeletePortfolioDependencyAsync(Guid id) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).From<PortfolioDependency>().Where(x => x.Id == id).Delete();

    public async Task ToggleTemplateAsync(Guid id, bool active) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).From<TaskTemplate>().Where(x => x.Id == id)
            .Set(x => x.IsActive, active).Update();

    public async Task ToggleRecurringAsync(Guid id, bool active)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var rule = await client.From<RecurringTaskRule>().Where(x => x.Id == id).Single()
            ?? throw new InvalidOperationException("Recorrência não encontrada.");
        if (active && ((rule.EndsAt.HasValue && rule.NextRunAt > rule.EndsAt) ||
            (rule.MaxOccurrences.HasValue && rule.OccurrencesCreated >= rule.MaxOccurrences)))
            throw new InvalidOperationException("A recorrência já atingiu seu limite. Edite o término antes de retomá-la.");
        await client.From<RecurringTaskRule>().Where(x => x.Id == id)
            .Set(x => x.IsActive, active).Set(x => x.UpdatedAt, DateTime.UtcNow).Update();
    }

    public async Task UpdateRecurringRuleAsync(Guid id, string title, string? description, string cadence,
        int intervalCount, DateTime nextRunAt, string timeZone, string targetStatus, string priority,
        Guid? assignedTo, int estimatedMinutes, int dueAfterDays, DateTime? endsAt, int? maxOccurrences)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Informe os dados da recorrência.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var rule = await client.From<RecurringTaskRule>().Where(x => x.Id == id).Single()
            ?? throw new InvalidOperationException("Recorrência não encontrada.");
        var board = await client.From<Board>().Where(x => x.Id == rule.BoardId).Single()
            ?? throw new InvalidOperationException("Projeto não encontrado.");
        if (!board.Settings.Any(x => x.Id == targetStatus)) throw new InvalidOperationException("A etapa selecionada não existe no projeto.");
        timeZone = NormalizeTimeZone(timeZone);
        var utcNextRun = ToUtc(nextRunAt, timeZone);
        DateTime? utcEndsAt = endsAt.HasValue ? ToUtc(endsAt.Value, timeZone) : null;
        if (utcEndsAt.HasValue && utcEndsAt < utcNextRun) throw new InvalidOperationException("O término deve ser posterior à próxima execução.");
        if (maxOccurrences.HasValue && maxOccurrences <= rule.OccurrencesCreated)
            throw new InvalidOperationException($"O limite deve ser maior que as {rule.OccurrencesCreated} ocorrências já geradas.");
#pragma warning disable CS8603
        await client.From<RecurringTaskRule>().Where(x => x.Id == id)
            .Set(x => x.Title, title.Trim()).Set(x => x.Description!, description?.Trim())
            .Set(x => x.Cadence, PlanningRules.NormalizeCadence(cadence)).Set(x => x.IntervalCount, Math.Clamp(intervalCount, 1, 365))
            .Set(x => x.NextRunAt, utcNextRun).Set(x => x.TimeZone, timeZone).Set(x => x.TargetStatus, targetStatus)
            .Set(x => x.Priority, PlanningRules.NormalizePriority(priority)).Set(x => x.AssignedTo, assignedTo)
            .Set(x => x.EstimatedMinutes, Math.Clamp(estimatedMinutes, 0, 100000)).Set(x => x.DueAfterDays, Math.Clamp(dueAfterDays, 0, 365))
            .Set(x => x.EndsAt, utcEndsAt).Set(x => x.MaxOccurrences, maxOccurrences.HasValue ? Math.Clamp(maxOccurrences.Value, 1, 10000) : null)
            .Set(x => x.UpdatedAt, DateTime.UtcNow).Update();
#pragma warning restore CS8603
    }

    private static DateTime ToUtc(DateTime value, string timeZone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(NormalizeTimeZone(timeZone));
        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), zone);
    }

    private static string NormalizeTimeZone(string? value) => value is "America/Sao_Paulo" or "UTC" ? value : "America/Sao_Paulo";

    private static int CalculateCapacity(Profile person, IReadOnlyCollection<WorkSchedule> schedules,
        IReadOnlyCollection<CompanyHoliday> holidays, IReadOnlyCollection<UserAbsence> absences,
        DateTime start, DateTime end)
    {
        var schedule = schedules.Where(x => x.UserId == person.Id && x.ValidFrom.Date <= end && (!x.ValidTo.HasValue || x.ValidTo.Value.Date >= start))
            .OrderByDescending(x => x.ValidFrom).FirstOrDefault();
        var workDays = (schedule?.WorkDays ?? "1,2,3,4,5").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var day) ? day : 0).Where(day => day is >= 1 and <= 7).ToHashSet();
        if (workDays.Count == 0) workDays = [1, 2, 3, 4, 5];
        var dailyMinutes = (schedule?.WeeklyCapacityMinutes ?? 2400) / workDays.Count;
        var unavailable = absences.Where(x => x.UserId == person.Id).ToList();
        var total = 0;
        for (var day = start.Date; day <= end.Date; day = day.AddDays(1))
        {
            var isoDay = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
            if (!workDays.Contains(isoDay)) continue;
            if (holidays.Any(x => x.HolidayDate.Date == day && (!x.TeamId.HasValue || x.TeamId == person.TeamId))) continue;
            if (unavailable.Any(x => x.StartsOn.Date <= day && x.EndsOn.Date >= day)) continue;
            total += dailyMinutes;
        }
        return total;
    }

    private static bool IsDependencyConflict(PortfolioDependency dependency, IReadOnlyCollection<Board> boards)
    {
        var predecessor = boards.FirstOrDefault(x => x.Id == dependency.PredecessorBoardId);
        var successor = boards.FirstOrDefault(x => x.Id == dependency.SuccessorBoardId);
        if (predecessor == null || successor == null) return false;
        var required = PlanningRules.RequiredSuccessorDate(dependency.DependencyType,
            predecessor.PlannedStart, predecessor.PlannedEnd, dependency.LagDays);
        var successorDate = PlanningRules.NormalizeDependencyType(dependency.DependencyType) is "finish_to_finish" or "start_to_finish"
            ? successor.PlannedEnd : successor.PlannedStart;
        return required.HasValue && successorDate.HasValue && successorDate.Value.Date < required.Value.Date;
    }

    public async Task<List<WorkspaceSearchResult>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) return [];
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.Rpc<List<WorkspaceSearchResult>>("search_workspace", new
        {
            search_query = query.Trim(), result_limit = 40
        });
        return response ?? [];
    }
}
