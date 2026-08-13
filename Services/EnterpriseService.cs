using PulseBoardMigration.Models;

namespace PulseBoardMigration.Services;

public class EnterpriseService
{
    private readonly SupabaseClientFactory _clientFactory;

    public EnterpriseService(SupabaseClientFactory clientFactory) => _clientFactory = clientFactory;

    public async Task<EnterprisePlanningViewModel> GetPlanningAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var boards = await client.From<Board>().Get();
        var profiles = await client.From<Profile>().Get();
        var teams = await client.From<Team>().Get();
        var holidays = await client.From<CompanyHoliday>().Get();
        var absences = await client.From<UserAbsence>().Get();
        var baselines = await client.From<ProjectBaseline>().Get();
        var dependencies = await client.From<PortfolioDependency>().Get();
        var templates = await client.From<TaskTemplate>().Get();
        var recurring = await client.From<RecurringTaskRule>().Get();
        return new EnterprisePlanningViewModel
        {
            Boards = boards.Models.OrderBy(x => x.Name).ToList(),
            Profiles = profiles.Models.Where(x => x.IsActive).OrderBy(x => x.FullName ?? x.Email).ToList(),
            Teams = teams.Models.OrderBy(x => x.Name).ToList(),
            Holidays = holidays.Models.OrderBy(x => x.HolidayDate).ToList(),
            Absences = absences.Models.OrderByDescending(x => x.StartsOn).ToList(),
            Baselines = baselines.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            PortfolioDependencies = dependencies.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            Templates = templates.Models.OrderBy(x => x.Name).ToList(),
            RecurringRules = recurring.Models.OrderBy(x => x.NextRunAt).ToList()
        };
    }

    public async Task AddHolidayAsync(DateTime date, string name, Guid? teamId, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Informe o nome do feriado.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<CompanyHoliday>().Insert(new CompanyHoliday
        {
            HolidayDate = date.Date, Name = name.Trim(), TeamId = teamId, CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task AddAbsenceAsync(Guid userId, string type, DateTime startsOn, DateTime endsOn, string? notes, Guid actorId)
    {
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
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId).Single()
            ?? throw new InvalidOperationException("Projeto não encontrado.");
        var tasks = await client.From<PulseTask>().Where(x => x.BoardId == boardId).Get();
        var existing = await client.From<ProjectBaseline>().Where(x => x.BoardId == boardId).Get();
        var version = existing.Models.Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
        var snapshot = new Dictionary<string, object?>
        {
            ["tasks"] = tasks.Models.Where(x => x.ArchivedAt == null).Select(x => new Dictionary<string, object?>
            {
                ["id"] = x.Id, ["title"] = x.Title, ["startDate"] = x.StartDate,
                ["dueDate"] = x.DueDate, ["estimatedMinutes"] = x.EstimatedMinutes, ["status"] = x.Status
            }).ToList(),
            ["capturedAt"] = DateTime.UtcNow
        };
        await client.From<ProjectBaseline>().Insert(new ProjectBaseline
        {
            BoardId = boardId, Version = version, Name = string.IsNullOrWhiteSpace(name) ? $"Baseline {version}" : name.Trim(),
            PlannedStart = board.PlannedStart, PlannedEnd = board.PlannedEnd, BudgetAmount = board.BudgetAmount,
            Snapshot = snapshot,
            CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
#pragma warning disable CS8603
        await client.From<Board>().Where(x => x.Id == boardId)
            .Set(x => x.BaselineStart, board.PlannedStart).Set(x => x.BaselineEnd, board.PlannedEnd).Update();
#pragma warning restore CS8603
    }

    public async Task AddPortfolioDependencyAsync(Guid predecessor, Guid successor, string type, int lagDays, Guid actorId)
    {
        if (predecessor == successor) throw new InvalidOperationException("Um projeto não pode depender dele mesmo.");
        var allowed = new[] { "finish_to_start", "start_to_start", "finish_to_finish", "start_to_finish" };
        if (!allowed.Contains(type)) type = "finish_to_start";
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<PortfolioDependency>().Insert(new PortfolioDependency
        {
            PredecessorBoardId = predecessor, SuccessorBoardId = successor, DependencyType = type,
            LagDays = Math.Clamp(lagDays, -365, 365), CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task SaveTemplateAsync(string name, string? description, Guid? boardId, Guid? teamId, int estimatedMinutes, string priority, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Informe o nome do modelo.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TaskTemplate>().Insert(new TaskTemplate
        {
            Name = name.Trim(), Description = description?.Trim(), BoardId = boardId, TeamId = teamId,
            Definition = new Dictionary<string, object?> { ["estimatedMinutes"] = Math.Max(0, estimatedMinutes), ["priority"] = priority },
            IsActive = true, CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task SaveRecurringRuleAsync(Guid boardId, string title, string? description, string cadence,
        int intervalCount, DateTime nextRunAt, Guid? assignedTo, int estimatedMinutes, int dueAfterDays, Guid actorId)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Informe o título da recorrência.");
        if (cadence is not ("daily" or "weekly" or "monthly")) cadence = "weekly";
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<RecurringTaskRule>().Insert(new RecurringTaskRule
        {
            BoardId = boardId, Title = title.Trim(), Description = description?.Trim(), Cadence = cadence,
            IntervalCount = Math.Clamp(intervalCount, 1, 365), NextRunAt = nextRunAt,
            AssignedTo = assignedTo, EstimatedMinutes = Math.Max(0, estimatedMinutes),
            DueAfterDays = Math.Clamp(dueAfterDays, 0, 365), IsActive = true, CreatedBy = actorId, CreatedAt = DateTime.UtcNow
        });
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
