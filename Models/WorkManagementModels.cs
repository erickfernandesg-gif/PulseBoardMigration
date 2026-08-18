using Postgrest.Attributes;
using Postgrest.Models;

namespace PulseBoardMigration.Models;

[Table("notifications")]
public class UserNotification : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("recipient_id")] public Guid RecipientId { get; set; }
    [Column("actor_id")] public Guid? ActorId { get; set; }
    [Column("task_id")] public Guid? TaskId { get; set; }
    [Column("board_id")] public Guid? BoardId { get; set; }
    [Column("type")] public string Type { get; set; } = string.Empty;
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("message")] public string? Message { get; set; }
    [Column("action_url")] public string? ActionUrl { get; set; }
    [Column("priority")] public string Priority { get; set; } = "normal";
    [Column("deduplication_key")] public string? DeduplicationKey { get; set; }
    [Column("read_at")] public DateTime? ReadAt { get; set; }
    [Column("archived_at")] public DateTime? ArchivedAt { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("task_assignments")]
public class TaskAssignment : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("from_user_id")] public Guid? FromUserId { get; set; }
    [Column("to_user_id")] public Guid ToUserId { get; set; }
    [Column("assigned_by")] public Guid AssignedBy { get; set; }
    [Column("stage")] public string Stage { get; set; } = string.Empty;
    [Column("status")] public string Status { get; set; } = "pending";
    [Column("notes")] public string? Notes { get; set; }
    [Column("acceptance_criteria")] public string? AcceptanceCriteria { get; set; }
    [Column("response_note")] public string? ResponseNote { get; set; }
    [Column("due_date")] public DateTime? DueDate { get; set; }
    [Column("estimated_minutes")] public int EstimatedMinutes { get; set; }
    [Column("requires_acceptance")] public bool RequiresAcceptance { get; set; }
    [Column("acceptance_by")] public Guid? AcceptanceBy { get; set; }
    [Column("accepted_at")] public DateTime? AcceptedAt { get; set; }
    [Column("completed_at")] public DateTime? CompletedAt { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}

[Table("task_followers")]
public class TaskFollower : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("user_id")] public Guid UserId { get; set; }
    [Column("reason")] public string Reason { get; set; } = "manual";
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("task_dependencies")]
public class TaskDependency : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("depends_on_task_id")] public Guid DependsOnTaskId { get; set; }
    [Column("dependency_type")] public string DependencyType { get; set; } = "finish_to_start";
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("project_milestones")]
public class ProjectMilestone : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("board_id")] public Guid BoardId { get; set; }
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("due_date")] public DateTime DueDate { get; set; }
    [Column("status")] public string Status { get; set; } = "planned";
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("work_schedules")]
public class WorkSchedule : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("user_id")] public Guid? UserId { get; set; }
    [Column("team_id")] public Guid? TeamId { get; set; }
    [Column("weekly_capacity_minutes")] public int WeeklyCapacityMinutes { get; set; } = 2400;
    [Column("work_days")] public string WorkDays { get; set; } = "1,2,3,4,5";
    [Column("valid_from")] public DateTime ValidFrom { get; set; }
    [Column("valid_to")] public DateTime? ValidTo { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("client_contracts")]
public class ClientContract : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("client_id")] public Guid ClientId { get; set; }
    [Column("board_id")] public Guid? BoardId { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("contract_type")] public string ContractType { get; set; } = "hourly";
    [Column("billing_rate")] public decimal BillingRate { get; set; }
    [Column("budget_amount")] public decimal? BudgetAmount { get; set; }
    [Column("included_minutes")] public int? IncludedMinutes { get; set; }
    [Column("starts_on")] public DateTime StartsOn { get; set; }
    [Column("ends_on")] public DateTime? EndsOn { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("billing_invoices")]
public class BillingInvoice : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("client_id")] public Guid ClientId { get; set; }
    [Column("contract_id")] public Guid? ContractId { get; set; }
    [Column("reference")] public string Reference { get; set; } = string.Empty;
    [Column("status")] public string Status { get; set; } = "draft";
    [Column("period_start")] public DateTime PeriodStart { get; set; }
    [Column("period_end")] public DateTime PeriodEnd { get; set; }
    [Column("due_date")] public DateTime? DueDate { get; set; }
    [Column("subtotal")] public decimal Subtotal { get; set; }
    [Column("total")] public decimal Total { get; set; }
    [Column("created_by")] public Guid CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("billing_invoice_items")]
public class BillingInvoiceItem : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("invoice_id")] public Guid InvoiceId { get; set; }
    [Column("time_log_id")] public Guid? TimeLogId { get; set; }
    [Column("description")] public string Description { get; set; } = string.Empty;
    [Column("minutes")] public int Minutes { get; set; }
    [Column("unit_rate")] public decimal UnitRate { get; set; }
    [Column("amount")] public decimal Amount { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("company_holidays")]
public class CompanyHoliday : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("holiday_date")] public DateTime HolidayDate { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("team_id")] public Guid? TeamId { get; set; }
    [Column("created_by")] public Guid? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("user_absences")]
public class UserAbsence : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("user_id")] public Guid UserId { get; set; }
    [Column("absence_type")] public string AbsenceType { get; set; } = "vacation";
    [Column("starts_on")] public DateTime StartsOn { get; set; }
    [Column("ends_on")] public DateTime EndsOn { get; set; }
    [Column("notes")] public string? Notes { get; set; }
    [Column("status")] public string Status { get; set; } = "approved";
    [Column("created_by")] public Guid? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("project_baselines")]
public class ProjectBaseline : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("board_id")] public Guid BoardId { get; set; }
    [Column("version")] public int Version { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("planned_start")] public DateTime? PlannedStart { get; set; }
    [Column("planned_end")] public DateTime? PlannedEnd { get; set; }
    [Column("budget_amount")] public decimal? BudgetAmount { get; set; }
    [Column("snapshot")] public Dictionary<string, object?> Snapshot { get; set; } = [];
    [Column("is_active")] public bool IsActive { get; set; }
    [Column("created_by")] public Guid? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("portfolio_dependencies")]
public class PortfolioDependency : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("predecessor_board_id")] public Guid PredecessorBoardId { get; set; }
    [Column("successor_board_id")] public Guid SuccessorBoardId { get; set; }
    [Column("predecessor_task_id")] public Guid? PredecessorTaskId { get; set; }
    [Column("successor_task_id")] public Guid? SuccessorTaskId { get; set; }
    [Column("dependency_type")] public string DependencyType { get; set; } = "finish_to_start";
    [Column("lag_days")] public int LagDays { get; set; }
    [Column("created_by")] public Guid? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("task_templates")]
public class TaskTemplate : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("board_id")] public Guid? BoardId { get; set; }
    [Column("team_id")] public Guid? TeamId { get; set; }
    [Column("definition")] public Dictionary<string, object?> Definition { get; set; } = [];
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("created_by")] public Guid? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("recurring_task_rules")]
public class RecurringTaskRule : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("board_id")] public Guid BoardId { get; set; }
    [Column("template_id")] public Guid? TemplateId { get; set; }
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("description")] public string? Description { get; set; }
    [Column("cadence")] public string Cadence { get; set; } = "weekly";
    [Column("interval_count")] public int IntervalCount { get; set; } = 1;
    [Column("next_run_at")] public DateTime NextRunAt { get; set; }
    [Column("time_zone")] public string TimeZone { get; set; } = "America/Sao_Paulo";
    [Column("target_status")] public string TargetStatus { get; set; } = "todo";
    [Column("priority")] public string Priority { get; set; } = "medium";
    [Column("assigned_to")] public Guid? AssignedTo { get; set; }
    [Column("estimated_minutes")] public int EstimatedMinutes { get; set; }
    [Column("due_after_days")] public int DueAfterDays { get; set; }
    [Column("is_active")] public bool IsActive { get; set; } = true;
    [Column("custom_fields")] public Dictionary<string, object?> CustomFields { get; set; } = [];
    [Column("created_by")] public Guid? CreatedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
    [Column("last_run_at")] public DateTime? LastRunAt { get; set; }
    [Column("ends_at")] public DateTime? EndsAt { get; set; }
    [Column("max_occurrences")] public int? MaxOccurrences { get; set; }
    [Column("occurrences_created")] public int OccurrencesCreated { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}

[Table("task_mentions")]
public class TaskMention : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("comment_id")] public Guid CommentId { get; set; }
    [Column("mentioned_user_id")] public Guid MentionedUserId { get; set; }
    [Column("mentioned_by")] public Guid MentionedBy { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("task_files")]
public class TaskFile : BaseModel
{
    [PrimaryKey("id", false)] public Guid Id { get; set; }
    [Column("task_id")] public Guid TaskId { get; set; }
    [Column("uploaded_by")] public Guid UploadedBy { get; set; }
    [Column("storage_path")] public string StoragePath { get; set; } = string.Empty;
    [Column("file_name")] public string FileName { get; set; } = string.Empty;
    [Column("content_type")] public string ContentType { get; set; } = string.Empty;
    [Column("file_size")] public long FileSize { get; set; }
    [Column("description")] public string? Description { get; set; }
    [Column("version")] public int Version { get; set; } = 1;
    [Column("previous_version_id")] public Guid? PreviousVersionId { get; set; }
    [Column("created_at")] public DateTime CreatedAt { get; set; }
}

[Table("notification_preferences")]
public class NotificationPreference : BaseModel
{
    [PrimaryKey("user_id", false)] public Guid UserId { get; set; }
    [Column("in_app")] public bool InApp { get; set; } = true;
    [Column("email_digest")] public bool EmailDigest { get; set; }
    [Column("due_reminders")] public bool DueReminders { get; set; } = true;
    [Column("budget_alerts")] public bool BudgetAlerts { get; set; } = true;
    [Column("mention_alerts")] public bool MentionAlerts { get; set; } = true;
    [Column("digest_hour")] public short DigestHour { get; set; } = 8;
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}

public class WorkspaceSearchResult : BaseModel
{
    [Column("kind")] public string Kind { get; set; } = string.Empty;
    [Column("id")] public Guid Id { get; set; }
    [Column("board_id")] public Guid BoardId { get; set; }
    [Column("title")] public string Title { get; set; } = string.Empty;
    [Column("snippet")] public string? Snippet { get; set; }
    [Column("action_url")] public string ActionUrl { get; set; } = string.Empty;
    [Column("rank")] public float Rank { get; set; }
}

public class MyWorkViewModel
{
    public Guid CurrentUserId { get; set; }
    public List<PulseTask> Tasks { get; set; } = [];
    public List<Board> Boards { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<TaskAssignment> Assignments { get; set; } = [];
    public List<TaskFollower> Followers { get; set; } = [];
    public Board? Board(Guid id) => Boards.FirstOrDefault(x => x.Id == id);
    public Profile? Person(Guid? id) => id.HasValue ? Profiles.FirstOrDefault(x => x.Id == id) : null;
    public TaskAssignment? ActiveAssignment(Guid taskId) => Assignments
        .Where(x => x.TaskId == taskId && x.Status is "pending" or "accepted")
        .OrderByDescending(x => x.CreatedAt).FirstOrDefault();
}

public class ManagementViewModel
{
    public Profile CurrentUser { get; set; } = new();
    public List<Profile> Profiles { get; set; } = [];
    public List<Team> Teams { get; set; } = [];
    public List<Board> Boards { get; set; } = [];
    public List<PulseTask> Tasks { get; set; } = [];
    public List<WorkSchedule> Schedules { get; set; } = [];
    public List<TaskAssignment> Assignments { get; set; } = [];
    public List<CompanyHoliday> Holidays { get; set; } = [];
    public List<UserAbsence> Absences { get; set; } = [];
    public List<PerformancePersonMetric> Performance { get; set; } = [];
}

public class ScheduleRow
{
    public Guid TaskId { get; set; }
    public Guid BoardId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string BoardName { get; set; } = string.Empty;
    public string? PersonName { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public decimal LeftPercent { get; set; }
    public decimal WidthPercent { get; set; }
    public bool IsOverdue { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsCritical { get; set; }
}

public class CompanyScheduleViewModel
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<ScheduleRow> Rows { get; set; } = [];
    public List<ProjectMilestone> Milestones { get; set; } = [];
    public List<Board> Boards { get; set; } = [];
    public List<PortfolioDependency> PortfolioDependencies { get; set; } = [];
    public Board? Board(Guid id) => Boards.FirstOrDefault(x => x.Id == id);
}

public class BillingViewModel
{
    public string Month { get; set; } = DateTime.UtcNow.ToString("yyyy-MM");
    public List<TimeLog> Logs { get; set; } = [];
    public List<PulseTask> Tasks { get; set; } = [];
    public List<Board> Boards { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<ClientAccount> Clients { get; set; } = [];
    public List<ClientContract> Contracts { get; set; } = [];
    public List<BillingInvoice> Invoices { get; set; } = [];
    public decimal ApprovedUnbilled => Logs.Where(x => x.ApprovalStatus == "approved" && x.BillingStatus == "unbilled" && x.IsBillable).Sum(x => x.BillableAmount);
}
