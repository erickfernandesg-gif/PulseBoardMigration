namespace PulseBoardMigration.Domain;

public static class WorkRules
{
    public static int EffectiveWeeklyCapacity(int weeklyMinutes, int unavailableWorkDays, int workDays = 5)
    {
        if (workDays <= 0) return 0;
        var capacity = Math.Clamp(weeklyMinutes, 0, 10080);
        var unavailable = Math.Clamp(unavailableWorkDays, 0, workDays);
        return Math.Max(0, capacity - unavailable * capacity / workDays);
    }

    public static decimal UtilizationPercent(int allocatedMinutes, int effectiveCapacityMinutes) =>
        effectiveCapacityMinutes <= 0 ? 0 : Math.Max(0, allocatedMinutes) * 100m / effectiveCapacityMinutes;

    public static decimal EstimateAccuracyPercent(int estimatedMinutes, int actualMinutes) =>
        estimatedMinutes <= 0 ? 0 : Math.Min(200m, Math.Max(0, actualMinutes) * 100m / estimatedMinutes);

    public static decimal BillableAmount(int minutes, decimal hourlyRate) =>
        Math.Max(0, minutes) * Math.Max(0, hourlyRate) / 60m;

    public static bool CanMutateBilledTimeLog(string billingStatus) =>
        string.Equals(billingStatus, "unbilled", StringComparison.OrdinalIgnoreCase);
}

public static class CriticalPathRules
{
    public static HashSet<Guid> Calculate(
        IEnumerable<(Guid Id, int Duration)> tasks,
        IEnumerable<(Guid TaskId, Guid DependsOnId)> dependencies)
    {
        var durations = tasks.ToDictionary(x => x.Id, x => Math.Max(1, x.Duration));
        var predecessors = dependencies
            .Where(x => durations.ContainsKey(x.TaskId) && durations.ContainsKey(x.DependsOnId))
            .GroupBy(x => x.TaskId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.DependsOnId).Distinct().ToList());
        var memo = new Dictionary<Guid, (long Total, Guid? Previous)>();
        var visiting = new HashSet<Guid>();

        (long Total, Guid? Previous) Visit(Guid id)
        {
            if (memo.TryGetValue(id, out var cached)) return cached;
            if (!visiting.Add(id)) return (durations[id], null);
            long best = 0;
            Guid? previous = null;
            if (predecessors.TryGetValue(id, out var list))
                foreach (var predecessor in list)
                {
                    var candidate = Visit(predecessor).Total;
                    if (candidate > best) { best = candidate; previous = predecessor; }
                }
            visiting.Remove(id);
            return memo[id] = (best + durations[id], previous);
        }

        foreach (var id in durations.Keys) Visit(id);
        var current = memo.OrderByDescending(x => x.Value.Total).Select(x => (Guid?)x.Key).FirstOrDefault();
        var result = new HashSet<Guid>();
        while (current.HasValue && result.Add(current.Value)) current = memo[current.Value].Previous;
        return result;
    }
}
