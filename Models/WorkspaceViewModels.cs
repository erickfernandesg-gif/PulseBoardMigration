namespace PulseBoardMigration.Models;

public class AdminViewModel
{
    public List<Profile> Profiles { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<UserRate> Rates { get; set; } = [];
    public List<ClientAccount> Clients { get; set; } = [];
    public Dictionary<Guid, int> TeamMemberCounts { get; set; } = [];
    public Dictionary<Guid, int> ClientContractCounts { get; set; } = [];
    public Dictionary<Guid, int> ClientInvoiceCounts { get; set; } = [];
    public bool IsManager { get; set; }
}

public class SettingsViewModel
{
    public Profile Profile { get; set; } = new();
    public NotificationPreference Notifications { get; set; } = new();
}

public class ReportRow
{
    public Guid BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int BlockedTasks { get; set; }
    public int LoggedMinutes { get; set; }
    public decimal Cost { get; set; }
}

public class ReportContributorRow
{
    public Guid BoardId { get; set; }
    public string BoardName { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public int LoggedMinutes { get; set; }
    public decimal Cost { get; set; }
    public int Entries { get; set; }
    public decimal EffectiveHourlyRate => LoggedMinutes <= 0 ? 0 : Cost * 60m / LoggedMinutes;
}

public class ReportsViewModel
{
    public List<ReportRow> Rows { get; set; } = [];
    public List<ReportContributorRow> Contributors { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public string? Month { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? UserId { get; set; }
    public int TotalTasks => Rows.Sum(r => r.TotalTasks);
    public int CompletedTasks => Rows.Sum(r => r.CompletedTasks);
    public int LoggedMinutes => Rows.Sum(r => r.LoggedMinutes);
    public decimal TotalCost => Rows.Sum(r => r.Cost);
}

public class ExecutiveMetric
{
    public string Name { get; set; } = string.Empty;
    public int EstimatedMinutes { get; set; }
    public int LoggedMinutes { get; set; }
    public decimal Cost { get; set; }
}

public class ExecutiveViewModel
{
    public string FromMonth { get; set; } = DateTime.UtcNow.AddMonths(-2).ToString("yyyy-MM");
    public string ToMonth { get; set; } = DateTime.UtcNow.ToString("yyyy-MM");
    public List<ExecutiveMetric> Projects { get; set; } = [];
    public List<ExecutiveMetric> Clients { get; set; } = [];
    public List<ExecutiveMetric> Workload { get; set; } = [];
    public List<PerformancePersonMetric> PeoplePerformance { get; set; } = [];
    public List<ProjectFinancialMetric> ProjectFinancials { get; set; } = [];
    public int EstimatedMinutes => Projects.Sum(x => x.EstimatedMinutes);
    public int LoggedMinutes => Projects.Sum(x => x.LoggedMinutes);
    public decimal TotalCost => Projects.Sum(x => x.Cost);
    public decimal TotalBilledRevenue => ProjectFinancials.Sum(x => x.BilledRevenue);
    public decimal TotalMargin => ProjectFinancials.Sum(x => x.GrossMargin);
}
