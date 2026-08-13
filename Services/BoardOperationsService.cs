using ClosedXML.Excel;
using Newtonsoft.Json;
using PulseBoardMigration.Models;
using System.Globalization;
using System.Security.Cryptography;

namespace PulseBoardMigration.Services;

public class BoardOperationsService
{
    private readonly SupabaseClientFactory _clientFactory;
    private readonly BoardService _boardService;

    public BoardOperationsService(SupabaseClientFactory clientFactory, BoardService boardService)
    {
        _clientFactory = clientFactory;
        _boardService = boardService;
    }

    public async Task<BoardOperationsViewModel?> GetAsync(Guid boardId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId).Single();
        if (board == null) return null;

        var boards = await client.From<Board>().Get();
        var tasks = await client.From<PulseTask>().Get();
        var profiles = await client.From<Profile>().Where(x => x.IsActive == true).Get();
        var forms = await client.From<IntakeFormDefinition>().Where(x => x.BoardId == boardId).Get();
        var automations = await client.From<AutomationRule>().Where(x => x.BoardId == boardId).Get();
        var approvals = await client.From<TaskApprovalStep>().Get();
        var delegations = await client.From<ApprovalDelegation>().Get();
        var mirrors = await client.From<TaskFieldMirror>().Get();
        var history = await client.From<TaskFieldHistory>().Where(x => x.BoardId == boardId).Get();
        var boardTaskIds = tasks.Models.Where(x => x.BoardId == boardId).Select(x => x.Id).ToHashSet();

        return new BoardOperationsViewModel
        {
            Board = board,
            Boards = boards.Models.Where(x => x.Status != "archived").OrderBy(x => x.Name).ToList(),
            Tasks = tasks.Models.Where(x => x.ArchivedAt == null).OrderBy(x => x.Title).ToList(),
            Profiles = profiles.Models.OrderBy(x => x.FullName ?? x.Email).ToList(),
            IntakeForms = forms.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            Automations = automations.Models.OrderByDescending(x => x.CreatedAt).ToList(),
            ApprovalSteps = approvals.Models.Where(x => boardTaskIds.Contains(x.TaskId)).OrderBy(x => x.Sequence).ToList(),
            Delegations = delegations.Models.OrderByDescending(x => x.StartsOn).ToList(),
            Mirrors = mirrors.Models.Where(x => boardTaskIds.Contains(x.SourceTaskId) || boardTaskIds.Contains(x.TargetTaskId)).ToList(),
            FieldHistory = history.Models.OrderByDescending(x => x.CreatedAt).Take(250).ToList()
        };
    }

    public async Task SaveColumnsAsync(Guid boardId, IReadOnlyList<string> ids, IReadOnlyList<string> titles,
        IReadOnlyList<string> colors, IReadOnlyList<int?> wipLimits, IReadOnlyList<bool> approvals)
    {
        if (ids.Count == 0 || ids.Count != titles.Count) throw new InvalidOperationException("Informe ao menos uma coluna válida.");
        var settings = ids.Select((id, index) => new BoardColumnSetting
        {
            Id = NormalizeColumnId(id, index),
            Title = string.IsNullOrWhiteSpace(titles[index]) ? $"Etapa {index + 1}" : titles[index].Trim(),
            Color = index < colors.Count && IsHexColor(colors[index]) ? colors[index] : "#6366f1",
            WipLimit = index < wipLimits.Count && wipLimits[index] > 0 ? wipLimits[index] : null,
            RequiresApproval = index < approvals.Count && approvals[index]
        }).ToList();
        if (settings.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Count)
            throw new InvalidOperationException("Os identificadores das colunas não podem se repetir.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<Board>().Where(x => x.Id == boardId).Set(x => x.Settings, settings).Update();
        if (response.Models.Count == 0) throw new InvalidOperationException("Quadro não encontrado ou sem permissão.");
    }

    public async Task BulkActionAsync(Guid boardId, Guid[] taskIds, string action, Guid? assignedTo,
        string? status, DateTime? dueDate, string? priority)
    {
        var ids = taskIds.Where(x => x != Guid.Empty).Distinct().Take(500).ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Selecione ao menos uma tarefa.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Rpc("bulk_manage_tasks", new
        {
            p_board_id = boardId, p_task_ids = ids, p_action = action,
            p_assigned_to = assignedTo, p_status = status, p_due_date = dueDate?.Date, p_priority = priority
        });
    }

    public async Task<string> CreateIntakeFormAsync(Guid boardId, string title, string? description,
        string targetStatus, string priority, bool requireEmail, Guid userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<IntakeFormDefinition>().Insert(new IntakeFormDefinition
        {
            BoardId = boardId, Title = title.Trim(), Description = description?.Trim(), PublicToken = token,
            TargetStatus = targetStatus, DefaultPriority = priority, RequireEmail = requireEmail,
            CreatedBy = userId, CreatedAt = DateTime.UtcNow
        });
        return token;
    }

    public async Task<IntakeFormDefinition?> GetPublicFormAsync(string token)
    {
        if (!ValidToken(token)) return null;
        var client = _clientFactory.CreateServiceClient();
        return await client.From<IntakeFormDefinition>().Where(x => x.PublicToken == token).Single();
    }

    public async Task<Guid> SubmitIntakeAsync(string token, string title, string? description,
        string requesterName, string? requesterEmail)
    {
        var form = await GetPublicFormAsync(token);
        if (form is not { IsActive: true }) throw new InvalidOperationException("Formulário indisponível.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200) throw new InvalidOperationException("Informe um título válido.");
        if (form.RequireEmail && string.IsNullOrWhiteSpace(requesterEmail)) throw new InvalidOperationException("Informe seu e-mail.");

        var client = _clientFactory.CreateServiceClient();
        var task = new PulseTask
        {
            Id = Guid.NewGuid(), BoardId = form.BoardId, Title = title.Trim(), Description = description?.Trim(),
            Status = form.TargetStatus, Priority = form.DefaultPriority, CreatedBy = form.CreatedBy,
            AccountableOwnerId = form.CreatedBy, WorkflowState = "waiting_external", CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow, StatusUpdatedAt = DateTime.UtcNow,
            CustomFields = new() { ["requester_name"] = requesterName.Trim(), ["requester_email"] = requesterEmail?.Trim(), ["intake_form_id"] = form.Id }
        };
        await client.From<PulseTask>().Insert(task);
        return task.Id;
    }

    public async Task AddApprovalStepAsync(Guid taskId, int sequence, Guid approverId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TaskApprovalStep>().Insert(new TaskApprovalStep
        { TaskId = taskId, Sequence = Math.Max(1, sequence), ApproverId = approverId, Status = sequence <= 1 ? "pending" : "waiting", CreatedAt = DateTime.UtcNow });
    }

    public async Task DecideApprovalAsync(Guid stepId, string decision, string? note) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).Rpc("decide_task_approval",
            new { p_step_id = stepId, p_decision = decision, p_note = note });

    public async Task AddDelegationAsync(Guid delegatorId, Guid substituteId, DateTime startsOn, DateTime endsOn, Guid userId)
    {
        if (delegatorId == substituteId || endsOn.Date < startsOn.Date) throw new InvalidOperationException("Período ou substituto inválido.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<ApprovalDelegation>().Insert(new ApprovalDelegation
        { DelegatorId = delegatorId, SubstituteId = substituteId, StartsOn = startsOn.Date, EndsOn = endsOn.Date, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
    }

    public async Task AddMirrorAsync(Guid sourceTaskId, Guid targetTaskId, string fieldName, Guid userId)
    {
        if (sourceTaskId == targetTaskId) throw new InvalidOperationException("Origem e destino devem ser diferentes.");
        var allowed = new[] { "status", "priority", "due_date", "assigned_to" };
        if (!allowed.Contains(fieldName)) throw new InvalidOperationException("Campo espelhado inválido.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TaskFieldMirror>().Insert(new TaskFieldMirror
        { SourceTaskId = sourceTaskId, TargetTaskId = targetTaskId, FieldName = fieldName, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
    }

    public async Task AddCrossProjectDependencyAsync(Guid taskId, Guid dependsOnTaskId)
    {
        if (taskId == dependsOnTaskId) throw new InvalidOperationException("Uma tarefa não pode depender dela mesma.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<TaskDependency>().Insert(new TaskDependency
        { TaskId = taskId, DependsOnTaskId = dependsOnTaskId, DependencyType = "finish_to_start", CreatedAt = DateTime.UtcNow });
    }

    public BoardImportPreviewViewModel ParseImport(Guid boardId, Stream stream, string fileName, string source)
    {
        var rows = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? ParseCsv(stream)
            : ParseXlsx(stream);
        if (rows.Count == 0) throw new InvalidOperationException("A planilha não contém linhas para importar.");
        var headers = rows.SelectMany(x => x.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new() { BoardId = boardId, Source = source, Headers = headers, Rows = rows.Take(100).ToList(), Payload = JsonConvert.SerializeObject(rows.Take(500)) };
    }

    public async Task<int> CommitImportAsync(Guid boardId, string payload, string titleColumn, string? descriptionColumn,
        string? statusColumn, string? priorityColumn, string? dueDateColumn, Guid userId)
    {
        var rows = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(payload) ?? [];
        var count = 0;
        foreach (var row in rows.Take(500))
        {
            var title = Value(row, titleColumn);
            if (string.IsNullOrWhiteSpace(title)) continue;
            DateTime? due = DateTime.TryParse(Value(row, dueDateColumn), CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var parsed) ? parsed.Date : null;
            await _boardService.CreateTaskAsync(new PulseTask
            {
                BoardId = boardId, Title = title, Description = Value(row, descriptionColumn),
                Status = NormalizeImportedStatus(Value(row, statusColumn)), Priority = NormalizeImportedPriority(Value(row, priorityColumn)),
                DueDate = due, CreatedBy = userId, AccountableOwnerId = userId, WorkflowState = "inbox"
            });
            count++;
        }
        return count;
    }

    private static List<Dictionary<string, string>> ParseXlsx(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var range = sheet.RangeUsed() ?? throw new InvalidOperationException("Planilha vazia.");
        var headers = range.FirstRow().Cells().Select(x => x.GetFormattedString().Trim()).ToList();
        return range.RowsUsed().Skip(1).Select(row => headers.Select((header, i) => (header, value: row.Cell(i + 1).GetFormattedString().Trim()))
            .Where(x => !string.IsNullOrWhiteSpace(x.header)).ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static List<Dictionary<string, string>> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        if (lines.Count == 0) return [];
        var delimiter = lines[0].Count(x => x == ';') > lines[0].Count(x => x == ',') ? ';' : ',';
        var headers = SplitCsv(lines[0], delimiter);
        return lines.Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).Select(line =>
        {
            var values = SplitCsv(line, delimiter);
            return headers.Select((header, i) => (header, value: i < values.Count ? values[i] : ""))
                .ToDictionary(x => x.header, x => x.value, StringComparer.OrdinalIgnoreCase);
        }).ToList();
    }

    private static List<string> SplitCsv(string line, char delimiter)
    {
        var result = new List<string>(); var current = ""; var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { current += '"'; i++; } else quoted = !quoted; }
            else if (line[i] == delimiter && !quoted) { result.Add(current.Trim()); current = ""; }
            else current += line[i];
        }
        result.Add(current.Trim()); return result;
    }

    private static string Value(Dictionary<string, string> row, string? key) =>
        !string.IsNullOrWhiteSpace(key) && row.TryGetValue(key, out var value) ? value.Trim() : "";
    private static string NormalizeImportedStatus(string value) => value.Trim().ToLowerInvariant() switch
    { "working on it" or "em andamento" or "em execução" or "in-progress" => "in-progress", "done" or "concluído" or "concluida" => "done", "homologação" or "teste" => "homologation", _ => "todo" };
    private static string NormalizeImportedPriority(string value) => value.Trim().ToLowerInvariant() switch
    { "critical" or "crítica" => "critical", "high" or "alta" => "high", "low" or "baixa" => "low", _ => "medium" };
    private static bool ValidToken(string value) => value.Length == 48 && value.All(Uri.IsHexDigit);
    private static bool IsHexColor(string value) => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private static string NormalizeColumnId(string value, int index)
    {
        var id = new string(value.Trim().ToLowerInvariant().Select(x => char.IsLetterOrDigit(x) ? x : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(id) ? $"stage-{index + 1}" : id;
    }
}
