using PulseBoardMigration.Models;
using Xunit;

namespace PulseBoardMigration.Tests;

public class MyWorkViewModelTests
{
    [Fact]
    public void PendingTaskDoesNotAlsoAppearAsActiveWork()
    {
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var model = new MyWorkViewModel
        {
            CurrentUserId = userId,
            Tasks =
            [
                new PulseTask
                {
                    Id = taskId, AssignedTo = userId, Status = "todo", WorkflowState = "inbox",
                    Title = "Nova atribuição"
                }
            ],
            Assignments =
            [
                new TaskAssignment
                {
                    Id = Guid.NewGuid(), TaskId = taskId, ToUserId = userId, Status = "pending",
                    CreatedAt = DateTime.UtcNow
                }
            ]
        };

        Assert.Single(model.PendingAssignments);
        Assert.Empty(model.ActiveTasks);
    }

    [Fact]
    public void AttentionCountUsesOneConsistentSetOfRules()
    {
        var userId = Guid.NewGuid();
        var pendingTaskId = Guid.NewGuid();
        var urgentTaskId = Guid.NewGuid();
        var reviewTaskId = Guid.NewGuid();
        var model = new MyWorkViewModel
        {
            CurrentUserId = userId,
            Tasks =
            [
                new PulseTask { Id = pendingTaskId, AssignedTo = userId, Status = "todo", WorkflowState = "inbox", Title = "Pendente" },
                new PulseTask { Id = urgentTaskId, AssignedTo = userId, Status = "in-progress", WorkflowState = "in_progress", DueDate = DateTime.UtcNow.Date.AddDays(-1), Title = "Atrasada" },
                new PulseTask { Id = reviewTaskId, AcceptanceBy = userId, AssignedTo = Guid.NewGuid(), Status = "homologation", WorkflowState = "waiting_review", Title = "Revisão" }
            ],
            Assignments =
            [
                new TaskAssignment { Id = Guid.NewGuid(), TaskId = pendingTaskId, ToUserId = userId, Status = "pending", CreatedAt = DateTime.UtcNow }
            ]
        };

        Assert.Single(model.PendingAssignments);
        Assert.Single(model.UrgentTasks);
        Assert.Single(model.ReviewTasks);
        Assert.Equal(3, model.AttentionCount);
    }
}
