using Newtonsoft.Json;
using Postgrest.Attributes;
using Postgrest.Models;
using System.ComponentModel.DataAnnotations;

namespace PulseBoardMigration.Models;

public class BoardColumnSetting
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;
}

[Table("boards")]
public class Board : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("name")]
    [Required(ErrorMessage = "O nome é obrigatório.")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("owner_id")]
    public Guid OwnerId { get; set; }

    [Column("status")]
    public string Status { get; set; } = "active";

    [Column("settings")]
    public List<BoardColumnSetting> Settings { get; set; } = [];

    [Column("planned_start")]
    public DateTime? PlannedStart { get; set; }

    [Column("planned_end")]
    public DateTime? PlannedEnd { get; set; }

    [Column("health")]
    public string Health { get; set; } = "on_track";

    [Column("budget_amount")]
    public decimal? BudgetAmount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
