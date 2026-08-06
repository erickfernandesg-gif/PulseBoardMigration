using Postgrest.Attributes;
using Postgrest.Models;
using System.ComponentModel.DataAnnotations;

namespace PulseBoardMigration.Models;

[Table("tasks")]
public class PulseTask : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("board_id")]
    public Guid BoardId { get; set; }

    [Column("title")]
    [Required(ErrorMessage = "O título é obrigatório.")]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("status")]
    public string Status { get; set; } = "todo";

    [Column("priority")]
    public string Priority { get; set; } = "medium";

    [Column("start_date")]
    public DateTime? StartDate { get; set; }

    [Column("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("assigned_to")]
    public Guid? AssignedTo { get; set; }

    [Column("accountable_owner_id")]
    public Guid? AccountableOwnerId { get; set; }

    [Column("created_by")]
    public Guid? CreatedBy { get; set; }

    [Column("workflow_state")]
    public string WorkflowState { get; set; } = "inbox";

    [Column("acceptance_by")]
    public Guid? AcceptanceBy { get; set; }

    [Column("accepted_at")]
    public DateTime? AcceptedAt { get; set; }

    [Column("position_index")]
    public int PositionIndex { get; set; }

    [Column("total_minutes_spent")]
    public int TotalMinutesSpent { get; set; }

    [Column("target_month")]
    public string? TargetMonth { get; set; }

    [Column("is_blocked")]
    public bool IsBlocked { get; set; }

    [Column("blocker_reason")]
    public string? BlockerReason { get; set; }

    [Column("client_id")]
    public Guid? ClientId { get; set; }

    [Column("estimated_minutes")]
    public int EstimatedMinutes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
