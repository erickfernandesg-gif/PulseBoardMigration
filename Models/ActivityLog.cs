using Postgrest.Attributes;
using Postgrest.Models;

namespace PulseBoardMigration.Models;

[Table("activity_log")]
public class ActivityLog : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("task_id")]
    public Guid? TaskId { get; set; }

    [Column("board_id")]
    public Guid? BoardId { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("details")]
    public Dictionary<string, object>? Details { get; set; }

    [Column("audit_hash")]
    public string? AuditHash { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
