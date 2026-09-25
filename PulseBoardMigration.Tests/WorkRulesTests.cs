using PulseBoardMigration.Domain;
using PulseBoardMigration.Models;
using Xunit;

namespace PulseBoardMigration.Tests;

public class WorkRulesTests
{
    [Fact]
    public void EffectiveCapacityDiscountsApprovedAbsence()
    {
        Assert.Equal(1440, WorkRules.EffectiveWeeklyCapacity(2400, 2));
    }

    [Fact]
    public void UtilizationCanExposeOverAllocation()
    {
        Assert.Equal(125m, WorkRules.UtilizationPercent(3000, 2400));
    }

    [Fact]
    public void EstimateAccuracyIsCappedToAvoidDistortedDashboard()
    {
        Assert.Equal(200m, WorkRules.EstimateAccuracyPercent(60, 600));
    }

    [Fact]
    public void EffortUsesSubtasksInsteadOfCountingParentAndChildren()
    {
        var parent = new PulseTask { Id = Guid.NewGuid(), EstimatedMinutes = 480 };
        var child = new PulseTask { Id = Guid.NewGuid(), ParentTaskId = parent.Id, EstimatedMinutes = 180 };

        var effort = WorkRules.LeafTasksForEffort([parent, child]);

        Assert.Single(effort);
        Assert.Equal(child.Id, effort[0].Id);
    }

    [Theory]
    [InlineData("invoiced", false)]
    [InlineData("written_off", false)]
    [InlineData("unbilled", true)]
    public void BilledLogsAreImmutable(string status, bool expected)
    {
        Assert.Equal(expected, WorkRules.CanMutateBilledTimeLog(status));
    }

    [Fact]
    public void BillableAmountUsesMinutePrecision()
    {
        Assert.Equal(187.50m, WorkRules.BillableAmount(90, 125m));
    }

    [Theory]
    [InlineData("draft", "issued", true)]
    [InlineData("draft", "cancelled", true)]
    [InlineData("issued", "paid", true)]
    [InlineData("paid", "draft", false)]
    [InlineData("issued", "cancelled", false)]
    public void InvoiceStatusUsesAnIrreversibleFinancialFlow(string current, string next, bool expected)
    {
        Assert.Equal(expected, BillingRules.CanTransitionInvoice(current, next));
    }

    [Fact]
    public void OnlyHourlyContractsAreEligibleForAutomaticBilling()
    {
        Assert.True(BillingRules.IsAutomaticBillingContract("hourly"));
        Assert.False(BillingRules.IsAutomaticBillingContract("fixed"));
    }

    [Fact]
    public void CriticalPathChoosesLongestDependencyChain()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var shortTask = Guid.NewGuid();
        var result = CriticalPathRules.Calculate(
            [(a, 60), (b, 120), (c, 30), (shortTask, 10)],
            [(b, a), (c, b), (c, shortTask)]);
        Assert.True(result.SetEquals([a, b, c]));
    }

    [Fact]
    public void PortfolioDependencyRejectsIndirectCycle()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        Assert.True(PlanningRules.WouldCreatePortfolioCycle(c, a, [(a, b), (b, c)]));
        Assert.False(PlanningRules.WouldCreatePortfolioCycle(a, c, [(a, b)]));
    }

    [Theory]
    [InlineData("critical", "critical")]
    [InlineData("unexpected", "medium")]
    public void PlanningPriorityIsNormalized(string value, string expected) =>
        Assert.Equal(expected, PlanningRules.NormalizePriority(value));

    [Fact]
    public void FinishToStartDependencyUsesPredecessorEndAndLag()
    {
        var required = PlanningRules.RequiredSuccessorDate(
            "finish_to_start", new DateTime(2026, 8, 1), new DateTime(2026, 8, 10), 2);
        Assert.Equal(new DateTime(2026, 8, 12), required);
    }
}
