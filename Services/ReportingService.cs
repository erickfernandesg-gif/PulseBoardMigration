using PulseBoardMigration.Models;
using PulseBoardMigration.Domain;

namespace PulseBoardMigration.Services;

public class ReportingService
{
    private readonly SupabaseClientFactory _clientFactory;

    public ReportingService(SupabaseClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }

    public async Task<ReportsViewModel> GetReportsAsync(
        string? month,
        Guid? teamId,
        Guid? userId)
    {
        var data = await LoadAsync();
        var tasks = data.Tasks.AsEnumerable();
        var logs = data.Logs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(month))
        {
            logs = logs.Where(log => log.LogDate.ToString("yyyy-MM") == month);
        }

        if (userId.HasValue)
        {
            tasks = tasks.Where(t => t.AssignedTo == userId);
            logs = logs.Where(l => l.UserId == userId);
        }

        if (teamId.HasValue)
        {
            var teamUsers = data.Profiles.Where(p => p.TeamId == teamId).Select(p => p.Id).ToHashSet();
            tasks = tasks.Where(t => t.AssignedTo.HasValue && teamUsers.Contains(t.AssignedTo.Value));
            logs = logs.Where(l => teamUsers.Contains(l.UserId));
        }

        var selectedLogs = logs.ToList();
        // This screen is a realization report: task counts use the same period as the logs.
        var selectedTaskIds = selectedLogs.Select(log => log.TaskId).ToHashSet();
        var selectedTasks = data.Tasks
            .Where(task => selectedTaskIds.Contains(task.Id))
            .ToList();

        var rows = data.Boards.Select(board =>
        {
            var boardTasks = selectedTasks.Where(t => t.BoardId == board.Id).ToList();
            var boardTaskIds = boardTasks.Select(t => t.Id).ToHashSet();
            var boardLogs = selectedLogs.Where(l => boardTaskIds.Contains(l.TaskId)).ToList();
            var cost = boardLogs.Sum(log =>
            {
                return EffectiveCost(log, data.Rates);
            });

            return new ReportRow
            {
                BoardId = board.Id,
                BoardName = board.Name,
                TotalTasks = boardTasks.Count,
                CompletedTasks = boardTasks.Count(t => t.Status == "done"),
                BlockedTasks = boardTasks.Count(t => t.IsBlocked),
                LoggedMinutes = boardLogs.Sum(l => l.Minutes),
                Cost = cost
            };
        }).Where(r => r.TotalTasks > 0).OrderByDescending(r => r.Cost).ToList();

        var contributors = selectedLogs
            .Join(data.Tasks, log => log.TaskId, task => task.Id, (log, task) => new { log, task })
            .Join(data.Boards, item => item.task.BoardId, board => board.Id, (item, board) => new { item.log, board })
            .GroupBy(item => new { item.board.Id, item.board.Name, item.log.UserId })
            .Select(group =>
            {
                var profile = data.Profiles.FirstOrDefault(item => item.Id == group.Key.UserId);
                return new ReportContributorRow
                {
                    BoardId = group.Key.Id,
                    BoardName = group.Key.Name,
                    UserId = group.Key.UserId,
                    PersonName = profile?.FullName ?? profile?.Email ?? "Pessoa não localizada",
                    LoggedMinutes = group.Sum(item => item.log.Minutes),
                    Cost = group.Sum(item => EffectiveCost(item.log, data.Rates)),
                    Entries = group.Count()
                };
            })
            .OrderByDescending(row => row.Cost)
            .ThenBy(row => row.BoardName)
            .ThenBy(row => row.PersonName)
            .ToList();

        return new ReportsViewModel
        {
            Rows = rows,
            Contributors = contributors,
            Teams = data.Teams,
            Profiles = data.Profiles,
            Month = month,
            TeamId = teamId,
            UserId = userId
        };
    }

    public async Task<ExecutiveViewModel> GetExecutiveAsync(string? fromMonth, string? toMonth)
    {
        var data = await LoadAsync();
        var from = string.IsNullOrWhiteSpace(fromMonth)
            ? DateTime.UtcNow.AddMonths(-2).ToString("yyyy-MM")
            : fromMonth;
        var to = string.IsNullOrWhiteSpace(toMonth)
            ? DateTime.UtcNow.ToString("yyyy-MM")
            : toMonth;
        var plannedTasks = data.Tasks
            .Where(t => !string.IsNullOrWhiteSpace(t.TargetMonth) &&
                        string.CompareOrdinal(t.TargetMonth, from) >= 0 &&
                        string.CompareOrdinal(t.TargetMonth, to) <= 0)
            .ToList();
        var logs = data.Logs.Where(l =>
            l.LogDate.ToString("yyyy-MM").CompareTo(from) >= 0 &&
            l.LogDate.ToString("yyyy-MM").CompareTo(to) <= 0).ToList();
        // Support boards often do not use a planning cycle. Their execution must still
        // appear in the executive view by the date the work was recorded.
        var loggedTaskIds = logs.Select(log => log.TaskId).ToHashSet();
        var tasks = plannedTasks.Concat(data.Tasks.Where(task => loggedTaskIds.Contains(task.Id)))
            .GroupBy(task => task.Id).Select(group => group.First()).ToList();

        ExecutiveMetric ProjectMetric(Board board)
        {
            var projectTasks = tasks.Where(t => t.BoardId == board.Id).ToList();
            return BuildMetric(board.Name, projectTasks, logs, data.Rates);
        }

        var projectMetrics = data.Boards
            .Select(ProjectMetric)
            .Where(m => m.EstimatedMinutes > 0 || m.LoggedMinutes > 0)
            .OrderByDescending(m => m.Cost)
            .ToList();
        var clientMetrics = data.Clients.Select(account =>
        {
            var clientTasks = tasks.Where(t => t.ClientId == account.Id).ToList();
            return BuildMetric(account.Name, clientTasks, logs, data.Rates);
        }).Where(m => m.EstimatedMinutes > 0 || m.LoggedMinutes > 0)
          .OrderByDescending(m => m.Cost)
          .ToList();
        var workload = data.Profiles.Select(profile =>
        {
            var owned = tasks.Where(t => t.AssignedTo == profile.Id).ToList();
            return BuildMetric(profile.FullName ?? profile.Email, owned, logs, data.Rates);
        }).Where(m => m.EstimatedMinutes > 0 || m.LoggedMinutes > 0)
          .OrderByDescending(m => m.EstimatedMinutes)
          .ToList();
        var reportingClient = await _clientFactory.CreateForCurrentUserAsync();
        var invoices = await reportingClient.From<BillingInvoice>().Get();
        var invoiceItems = await reportingClient.From<BillingInvoiceItem>().Get();
        var issuedInvoiceIds = invoices.Models
            .Where(x => x.Status is "issued" or "paid")
            .Select(x => x.Id)
            .ToHashSet();
        var financials = data.Boards.Select(board =>
        {
            var allBoardTasks = tasks.Where(x => x.BoardId == board.Id).ToList();
            var boardTasks = WorkRules.LeafTasksForEffort(allBoardTasks);
            var ids = allBoardTasks.Select(x => x.Id).ToHashSet();
            var boardLogs = logs.Where(x => ids.Contains(x.TaskId)).ToList();
            var actual = boardLogs.Sum(x => x.CostRateSnapshot * x.Minutes / 60m);
            var committed = boardTasks.Where(x => x.Status != "done").Sum(task =>
            {
                var rate = data.Rates.FirstOrDefault(x => x.UserId == task.AssignedTo)?.HourlyRate ?? 0;
                return Math.Max(0, task.EstimatedMinutes - task.TotalMinutesSpent) * rate / 60m;
            });
            var boardLogIds = boardLogs.Select(x => x.Id).ToHashSet();
            var billed = invoiceItems.Models
                .Where(x => x.TimeLogId.HasValue && boardLogIds.Contains(x.TimeLogId.Value) && issuedInvoiceIds.Contains(x.InvoiceId))
                .Sum(x => x.Amount);
            var completion = boardTasks.Sum(x => x.EstimatedMinutes) == 0 ? 0 : boardTasks.Sum(x => Math.Min(x.TotalMinutesSpent, x.EstimatedMinutes)) * 100m / boardTasks.Sum(x => x.EstimatedMinutes);
            return new ProjectFinancialMetric
            {
                BoardId = board.Id, Name = board.Name, Budget = board.BudgetAmount ?? 0,
                RevenueBudget = board.RevenueBudget ?? 0, ActualCost = actual, CommittedCost = committed,
                BilledRevenue = billed, ForecastAtCompletion = actual + committed,
                ForecastEnd = board.ForecastEnd ?? (completion > 0 && board.PlannedStart.HasValue
                    ? board.PlannedStart.Value.AddDays((DateTime.UtcNow.Date - board.PlannedStart.Value.Date).TotalDays * 100 / (double)completion)
                    : board.PlannedEnd)
            };
        }).Where(x => x.Budget > 0 || x.ActualCost > 0 || x.BilledRevenue > 0).OrderByDescending(x => x.ActualCost).ToList();

        var people = data.Profiles.Where(x => x.IsActive).Select(profile =>
        {
            var owned = tasks.Where(x => x.AssignedTo == profile.Id).ToList();
            var ownedEffort = WorkRules.LeafTasksForEffort(owned);
            var completed = owned.Where(x => x.Status == "done").ToList();
            var onTime = completed.Count(x => !x.DueDate.HasValue || x.CompletedAt <= x.DueDate);
            return new PerformancePersonMetric
            {
                UserId = profile.Id, Name = profile.FullName ?? profile.Email, CompletedTasks = completed.Count,
                OpenTasks = owned.Count(x => x.Status != "done"), OverdueTasks = owned.Count(x => x.Status != "done" && x.DueDate < DateTime.UtcNow.Date),
                BlockedTasks = owned.Count(x => x.IsBlocked), EstimatedMinutes = ownedEffort.Sum(x => x.EstimatedMinutes),
                LoggedMinutes = owned.Sum(x => x.TotalMinutesSpent), OnTimePercent = completed.Count == 0 ? 0 : onTime * 100m / completed.Count,
                EstimateAccuracyPercent = ownedEffort.Sum(x => x.EstimatedMinutes) == 0 ? 0 : Math.Min(200, owned.Sum(x => x.TotalMinutesSpent) * 100m / ownedEffort.Sum(x => x.EstimatedMinutes))
            };
        }).OrderByDescending(x => x.CompletedTasks).ToList();

        return new ExecutiveViewModel
        {
            FromMonth = from,
            ToMonth = to,
            Projects = projectMetrics,
            Clients = clientMetrics,
            Workload = workload,
            PeoplePerformance = people,
            ProjectFinancials = financials
        };
    }

    private async Task<ReportingData> LoadAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var boards = await client.From<Board>().Get();
        var tasks = await client.From<PulseTask>().Get();
        var logs = await client.From<TimeLog>().Get();
        var rates = await client.From<UserRate>().Get();
        var profiles = await client.From<Profile>().Get();
        var teams = await client.From<Team>().Get();
        var clients = await client.From<ClientAccount>().Get();

        return new ReportingData(
            boards.Models.ToList(),
            tasks.Models.ToList(),
            logs.Models.ToList(),
            rates.Models.ToList(),
            profiles.Models.ToList(),
            teams.Models.ToList(),
            clients.Models.ToList());
    }

    private static ExecutiveMetric BuildMetric(
        string name,
        List<PulseTask> tasks,
        List<TimeLog> logs,
        List<UserRate> rates)
    {
        var ids = tasks.Select(t => t.Id).ToHashSet();
        var selectedLogs = logs.Where(l => ids.Contains(l.TaskId)).ToList();
        var cost = selectedLogs.Sum(log =>
        {
            return EffectiveCost(log, rates);
        });

        return new ExecutiveMetric
        {
            Name = name,
            EstimatedMinutes = WorkRules.LeafTasksForEffort(tasks).Sum(t => t.EstimatedMinutes),
            LoggedMinutes = selectedLogs.Sum(l => l.Minutes),
            Cost = cost
        };
    }

    private static decimal EffectiveCost(TimeLog log, List<UserRate> rates)
    {
        var rate = log.CostRateSnapshot > 0
            ? log.CostRateSnapshot
            : rates.FirstOrDefault(item => item.UserId == log.UserId)?.HourlyRate ?? 0;
        return rate * log.Minutes / 60m;
    }

    private sealed record ReportingData(
        List<Board> Boards,
        List<PulseTask> Tasks,
        List<TimeLog> Logs,
        List<UserRate> Rates,
        List<Profile> Profiles,
        List<Team> Teams,
        List<ClientAccount> Clients);
}
