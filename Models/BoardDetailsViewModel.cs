namespace PulseBoardMigration.Models;

public class BoardDetailsViewModel
{
    public Guid CurrentUserId { get; set; }
    public Board Board { get; set; } = new();
    public List<PulseTask> Tasks { get; set; } = [];
    public List<Column> Columns { get; set; } = [];
    public List<Profile> Profiles { get; set; } = [];
    public List<ClientAccount> Clients { get; set; } = [];
    public List<TaskCollaborator> Collaborators { get; set; } = [];
    public List<TaskComment> Comments { get; set; } = [];
    public List<TaskCommentAttachment> CommentAttachments { get; set; } = [];
    public List<TimeLog> TimeLogs { get; set; } = [];
    public List<TaskChecklist> Checklists { get; set; } = [];
    public List<ActivityLog> Activity { get; set; } = [];
    public List<TaskAssignment> Assignments { get; set; } = [];
    public List<TaskDependency> Dependencies { get; set; } = [];

    public Profile? Profile(Guid? id) =>
        id.HasValue ? Profiles.FirstOrDefault(p => p.Id == id.Value) : null;

    public ClientAccount? Client(Guid? id) =>
        id.HasValue ? Clients.FirstOrDefault(c => c.Id == id.Value) : null;
}
