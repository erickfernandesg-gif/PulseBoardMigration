using Postgrest.Attributes;
using Postgrest.Models;

namespace PulseBoardMigration.Models;

[Table("profiles")]
public class Profile : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("full_name")]
    public string? FullName { get; set; }

    [Column("role")]
    public string Role { get; set; } = "user";

    [Column("avatar_url")]
    public string? AvatarUrl { get; set; }

    [Column("team_id")]
    public Guid? TeamId { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("deactivated_at")]
    public DateTime? DeactivatedAt { get; set; }

    [Column("deactivated_by")]
    public Guid? DeactivatedBy { get; set; }

    [Column("last_read_notifications_at")]
    public DateTime? LastReadNotificationsAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("teams")]
public class Team : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("user_rates")]
public class UserRate : BaseModel
{
    [PrimaryKey("user_id", false)]
    public Guid UserId { get; set; }

    [Column("hourly_rate")]
    public decimal HourlyRate { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}

[Table("clients")]
public class ClientAccount : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("email")]
    public string? Email { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("time_logs")]
public class TimeLog : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("task_id")]
    public Guid TaskId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("minutes")]
    public int Minutes { get; set; }

    [Column("log_date")]
    public DateTime LogDate { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("is_billable")]
    public bool IsBillable { get; set; } = true;

    [Column("approval_status")]
    public string ApprovalStatus { get; set; } = "pending";

    [Column("billing_status")]
    public string BillingStatus { get; set; } = "unbilled";

    [Column("cost_rate_snapshot")]
    public decimal CostRateSnapshot { get; set; }

    [Column("billing_rate_snapshot")]
    public decimal BillingRateSnapshot { get; set; }

    [Column("approved_by")]
    public Guid? ApprovedBy { get; set; }

    [Column("approved_at")]
    public DateTime? ApprovedAt { get; set; }

    [Column("invoice_id")]
    public Guid? InvoiceId { get; set; }

    public decimal CostAmount => CostRateSnapshot * Minutes / 60m;
    public decimal BillableAmount => BillingRateSnapshot * Minutes / 60m;

    [Column("audit_hash")]
    public string? AuditHash { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("task_collaborators")]
public class TaskCollaborator : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("task_id")]
    public Guid TaskId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("role")]
    public string Role { get; set; } = "collaborator";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("task_comments")]
public class TaskComment : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("task_id")]
    public Guid TaskId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("message_type")]
    public string MessageType { get; set; } = "message";

    [Column("reply_to_id")]
    public Guid? ReplyToId { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("task_comment_attachments")]
public class TaskCommentAttachment : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("comment_id")]
    public Guid CommentId { get; set; }

    [Column("task_id")]
    public Guid TaskId { get; set; }

    [Column("uploaded_by")]
    public Guid UploadedBy { get; set; }

    [Column("storage_path")]
    public string StoragePath { get; set; } = string.Empty;

    [Column("file_name")]
    public string FileName { get; set; } = string.Empty;

    [Column("content_type")]
    public string ContentType { get; set; } = string.Empty;

    [Column("file_size")]
    public long FileSize { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

public sealed record CommentImageUpload(string FileName, string ContentType, byte[] Content);
public sealed record CommentAttachmentContent(string FileName, string ContentType, byte[] Content);

[Table("task_checklists")]
public class TaskChecklist : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("task_id")]
    public Guid TaskId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("is_completed")]
    public bool IsCompleted { get; set; }

    [Column("position_index")]
    public int PositionIndex { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}

[Table("automations")]
public class AutomationRule : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("trigger_type")]
    public string TriggerType { get; set; } = "status_change";

    [Column("trigger_value")]
    public string TriggerValue { get; set; } = "done";

    [Column("action_type")]
    public string ActionType { get; set; } = "notify_manager";

    [Column("action_payload")]
    public string? ActionPayload { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("board_id")]
    public Guid? BoardId { get; set; }

    [Column("condition_field")]
    public string? ConditionField { get; set; }
}
