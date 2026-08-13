namespace PulseBoardMigration.Models;

public class EnterprisePlanningViewModel
{
    public List<Board> Boards { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<CompanyHoliday> Holidays { get; set; } = [];
    public List<UserAbsence> Absences { get; set; } = [];
    public List<ProjectBaseline> Baselines { get; set; } = [];
    public List<PortfolioDependency> PortfolioDependencies { get; set; } = [];
    public List<TaskTemplate> Templates { get; set; } = [];
    public List<RecurringTaskRule> RecurringRules { get; set; } = [];
    public Board? Board(Guid id) => Boards.FirstOrDefault(x => x.Id == id);
    public Profile? Person(Guid id) => Profiles.FirstOrDefault(x => x.Id == id);
}

public class PerformancePersonMetric
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CompletedTasks { get; set; }
    public int OpenTasks { get; set; }
    public int OverdueTasks { get; set; }
    public int BlockedTasks { get; set; }
    public int EstimatedMinutes { get; set; }
    public int LoggedMinutes { get; set; }
    public decimal OnTimePercent { get; set; }
    public decimal EstimateAccuracyPercent { get; set; }
    public decimal ReworkPercent { get; set; }
    public decimal UtilizationPercent { get; set; }
    public decimal AvailableHours { get; set; }
}

public class ProjectFinancialMetric
{
    public Guid BoardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public decimal RevenueBudget { get; set; }
    public decimal ActualCost { get; set; }
    public decimal CommittedCost { get; set; }
    public decimal BilledRevenue { get; set; }
    public decimal GrossMargin => BilledRevenue - ActualCost;
    public decimal BudgetConsumedPercent => Budget <= 0 ? 0 : ActualCost / Budget * 100;
    public decimal ForecastAtCompletion { get; set; }
    public DateTime? ForecastEnd { get; set; }
}
