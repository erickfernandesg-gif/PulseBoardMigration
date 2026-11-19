namespace PulseBoardMigration.Models;

public class BoardDetailsViewModel
{
    public Guid CurrentUserId { get; set; }
    public Board Board { get; set; } = new();
    public List<PulseTask> Tasks { get; set; } = new();
    public List<PulseTask> ArchivedTasks { get; set; } = new();
    public List<Column> Columns { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public List<ClientAccount> Clients { get; set; } = new();
    public List<TaskCollaborator> Collaborators { get; set; } = new();
    public List<TaskComment> Comments { get; set; } = new();
    public List<TaskCommentAttachment> CommentAttachments { get; set; } = new();
    public List<TimeLog> TimeLogs { get; set; } = new();
    public List<TaskChecklist> Checklists { get; set; } = new();
    public List<ActivityLog> Activity { get; set; } = new();
    public List<TaskAssignment> Assignments { get; set; } = new();
    public List<TaskDependency> Dependencies { get; set; } = new();
    // Pré-requisitos de outros projetos, carregados separadamente para que o
    // detalhe da tarefa explique bloqueios externos sem misturá-los ao Kanban.
    public List<PulseTask> ExternalDependencyTasks { get; set; } = new();
    public List<Board> ExternalDependencyBoards { get; set; } = new();
    public List<TaskFile> Files { get; set; } = new();
    public List<TaskApprovalStep> ApprovalSteps { get; set; } = new();
    public List<ApprovalDelegation> ApprovalDelegations { get; set; } = new();
    public List<TaskTemplate> TaskTemplates { get; set; } = new();
    public bool CanManageBoard
    {
        get
        {
            var current = Profile(CurrentUserId);
            var owner = Profile(Board.OwnerId);
            return Board.OwnerId == CurrentUserId || current?.Role == "admin" ||
                (current?.Role == "manager" && current.TeamId.HasValue && current.TeamId == owner?.TeamId);
        }
    }

    public Profile? Profile(Guid? id) =>
        id.HasValue ? Profiles.FirstOrDefault(p => p.Id == id.Value) : null;

    public bool CanEditTask(PulseTask task)
    {
        var current = Profile(CurrentUserId);
        var owner = Profile(Board.OwnerId);
        var assignee = Profile(task.AssignedTo);
        if (current is not { IsActive: true }) return false;
        return current.Role == "admin" || Board.OwnerId == CurrentUserId ||
            task.AssignedTo == CurrentUserId || task.AccountableOwnerId == CurrentUserId ||
            task.CreatedBy == CurrentUserId || Collaborators.Any(x => x.TaskId == task.Id && x.UserId == CurrentUserId) ||
            (current.Role == "manager" && current.TeamId.HasValue &&
             (current.TeamId == assignee?.TeamId || current.TeamId == owner?.TeamId));
    }

    public ClientAccount? Client(Guid? id) =>
        id.HasValue ? Clients.FirstOrDefault(c => c.Id == id.Value) : null;

    public PulseTask? DependencyTask(Guid id) =>
        Tasks.FirstOrDefault(task => task.Id == id) ?? ExternalDependencyTasks.FirstOrDefault(task => task.Id == id);

    public Board? DependencyBoard(Guid id) =>
        id == Board.Id ? Board : ExternalDependencyBoards.FirstOrDefault(board => board.Id == id);

    public bool CanDecide(TaskApprovalStep step) =>
        CanManageBoard || step.ApproverId == CurrentUserId || ApprovalDelegations.Any(delegation =>
            delegation.DelegatorId == step.ApproverId && delegation.SubstituteId == CurrentUserId && delegation.IsActive &&
            DateTime.Today >= delegation.StartsOn.Date && DateTime.Today <= delegation.EndsOn.Date);
}
