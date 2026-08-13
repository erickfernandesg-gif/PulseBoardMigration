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

    [JsonProperty("color")]
    public string Color { get; set; } = "#6366f1";

    [JsonProperty("wip_limit")]
    public int? WipLimit { get; set; }

    [JsonProperty("requires_approval")]
    public bool RequiresApproval { get; set; }
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

    [Column("baseline_start")]
    public DateTime? BaselineStart { get; set; }

    [Column("baseline_end")]
    public DateTime? BaselineEnd { get; set; }

    [Column("forecast_end")]
    public DateTime? ForecastEnd { get; set; }

    [Column("revenue_budget")]
    public decimal? RevenueBudget { get; set; }

    [Column("budget_warning_percent")]
    public int BudgetWarningPercent { get; set; } = 80;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
