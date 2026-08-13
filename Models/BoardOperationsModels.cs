using Postgrest.Attributes;
using Postgrest.Models;

namespace PulseBoardMigration.Models;

[Table("task_field_history")]
public class TaskFieldHistory : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("board_id")] public Guid BoardId { get; set; }
    [Column("changed_by")] public Guid? ChangedBy { get; set; }
    [Column("field_name")] public string FieldName { get; set; } = string.Empty;
    [Column("old_value")] public object? OldValue { get; set; }
    [Column("new_value")] public object? NewValue { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("intake_forms")]
public class IntakeFormDefinition : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("board_id")] public Guid BoardId { get; set; }
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("public_token")] public string PublicToken { get; set; } = string.Empty;
    [Column("target_status")] public string TargetStatus { get; set; } = "backlog";
    [Column("default_priority")] public string DefaultPriority { get; set; } = "medium";
    [Column("require_email")] public bool RequireEmail { get; set; } = true;
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_by")] public Guid CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("task_approval_steps")]
public class TaskApprovalStep : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("sequence")] public int Sequence { get; set; }
    [Column("approver_id")] public Guid ApproverId { get; set; }
    [Column("status")] public string Status { get; set; } = "waiting";
    [Column("decision_by")] public Guid? DecisionBy { get; set; }
    [Column("decision_note")] public string? DecisionNote { get; set; }
    [Column("decided_at")] public DateTime? DecidedAt { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("approval_delegations")]
public class ApprovalDelegation : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("delegator_id")] public Guid DelegatorId { get; set; }
    [Column("substitute_id")] public Guid SubstituteId { get; set; }
    [Column("starts_on")] public DateTime StartsOn { get; set; }
    [Column("ends_on")] public DateTime EndsOn { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_by")] public Guid CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("task_field_mirrors")]
public class TaskFieldMirror : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("source_task_id")] public Guid SourceTaskId { get; set; }
    [Column("target_task_id")] public Guid TargetTaskId { get; set; }
    [Column("field_name")] public string FieldName { get; set; } = "status";
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_by")] public Guid CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

public class BoardOperationsViewModel
{
    public Board Board { get; set; } = new();
    public List<Board> Boards { get; set; } = [];
    public List<PulseTask> Tasks { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<IntakeFormDefinition> IntakeForms { get; set; } = [];
    public List<AutomationRule> Automations { get; set; } = [];
    public List<TaskApprovalStep> ApprovalSteps { get; set; } = [];
    public List<ApprovalDelegation> Delegations { get; set; } = [];
    public List<TaskFieldMirror> Mirrors { get; set; } = [];
    public List<TaskFieldHistory> FieldHistory { get; set; } = [];
}

public class IntakePublicViewModel
{
    public string Token { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool RequireEmail { get; set; }
}

public class BoardImportPreviewViewModel
{
    public Guid BoardId { get; set; }
    public string Source { get; set; } = "excel";
    public List<string> Headers { get; set; } = [];
    public List<Dictionary<string, string>> Rows { get; set; } = [];
    public string Payload { get; set; } = string.Empty;
}
