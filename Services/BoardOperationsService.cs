using ClosedXML.Excel;
using Newtonsoft.Json;
using PulseBoardMigration.Models;
using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

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

    public async Task<bool> CanManageBoardAsync(Guid boardId, Guid userId, bool privileged)
    {
        var board = await (await _clientFactory.CreateForCurrentUserAsync()).From<Board>().Where(x => x.Id == boardId).Single();
        return board != null && (privileged || board.OwnerId == userId);
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
        var dependencies = await client.From<TaskDependency>().Get();
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
            CrossDependencies = dependencies.Models.Where(x => boardTaskIds.Contains(x.TaskId) &&
                tasks.Models.Any(task => task.Id == x.DependsOnTaskId && task.BoardId != boardId)).ToList(),
            FieldHistory = history.Models.OrderByDescending(x => x.CreatedAt).Take(250).ToList()
        };
    }

    public async Task SaveColumnsAsync(Guid boardId, IReadOnlyList<string> ids, IReadOnlyList<string> titles,
        IReadOnlyList<string> colors, IReadOnlyList<int?> wipLimits, IReadOnlyList<bool> approvals)
    {
        if (ids.Count == 0 || ids.Count > 20 || ids.Count != titles.Count)
            throw new InvalidOperationException("Informe entre 1 e 20 colunas válidas.");
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
        if (settings.Select(x => x.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Count)
            throw new InvalidOperationException("Os nomes das colunas não podem se repetir.");

        var client = await _clientFactory.CreateForCurrentUserAsync();
        _ = await client.From<Board>().Where(x => x.Id == boardId).Single()
            ?? throw new InvalidOperationException("Quadro não encontrado ou sem permissão.");
        var taskResponse = await client.From<PulseTask>().Where(x => x.BoardId == boardId).Get();
        var activeTasks = taskResponse.Models.Where(x => x.ArchivedAt == null).ToList();
        var missingStatuses = activeTasks.Select(x => x.Status).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(status => settings.All(column => !string.Equals(column.Id, status, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missingStatuses.Count > 0)
            throw new InvalidOperationException($"Não é possível remover ou renomear etapas que possuem tarefas: {string.Join(", ", missingStatuses)}.");
        var exceeded = settings.FirstOrDefault(column => column.WipLimit.HasValue &&
            activeTasks.Count(task => string.Equals(task.Status, column.Id, StringComparison.OrdinalIgnoreCase)) > column.WipLimit.Value);
        if (exceeded != null)
            throw new InvalidOperationException($"A etapa {exceeded.Title} já possui mais tarefas que o limite WIP informado.");

        var response = await client.From<Board>().Where(x => x.Id == boardId).Set(x => x.Settings, settings).Update();
        if (response.Models.Count == 0) throw new InvalidOperationException("Quadro não encontrado ou sem permissão.");
    }

    public async Task BulkActionAsync(Guid boardId, Guid[] taskIds, string action, Guid? assignedTo,
        string? status, DateTime? dueDate, string? priority)
    {
        var ids = taskIds.Where(x => x != Guid.Empty).Distinct().Take(500).ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Selecione ao menos uma tarefa.");
        var allowedActions = new[] { "assign", "move", "archive", "due_date", "priority" };
        if (!allowedActions.Contains(action)) throw new InvalidOperationException("Ação em massa inválida.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId).Single()
            ?? throw new InvalidOperationException("Quadro não encontrado ou sem permissão.");
        if (action == "move" && board.Settings.All(x => x.Id != status))
            throw new InvalidOperationException("Selecione uma etapa válida deste Board.");
        if (action == "priority" && priority is not ("low" or "medium" or "high" or "critical"))
            throw new InvalidOperationException("Selecione uma prioridade válida.");
        if (action == "assign" && assignedTo.HasValue)
        {
            var person = await client.From<Profile>().Where(x => x.Id == assignedTo.Value).Single();
            if (person is not { IsActive: true }) throw new InvalidOperationException("O responsável selecionado não está ativo.");
        }
        await client.Rpc("bulk_manage_tasks", new
        {
            p_board_id = boardId, p_task_ids = ids, p_action = action,
            p_assigned_to = assignedTo, p_status = status, p_due_date = dueDate?.Date, p_priority = priority
        });
    }

    public async Task<string> CreateIntakeFormAsync(Guid boardId, string title, string? description,
        string targetStatus, string priority, bool requireEmail, Guid userId)
    {
        title = title?.Trim() ?? string.Empty;
        if (title.Length is < 1 or > 200) throw new InvalidOperationException("Informe um nome de formulário válido.");
        if (priority is not ("low" or "medium" or "high" or "critical")) throw new InvalidOperationException("Prioridade inválida.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId).Single()
            ?? throw new InvalidOperationException("Quadro não encontrado ou sem permissão.");
        if (board.Settings.All(x => x.Id != targetStatus)) throw new InvalidOperationException("A etapa de destino não existe neste Board.");
        await client.From<IntakeFormDefinition>().Insert(new IntakeFormDefinition
        {
            BoardId = boardId, Title = title, Description = description?.Trim(), PublicToken = token,
            TargetStatus = targetStatus, DefaultPriority = priority, RequireEmail = requireEmail,
            CreatedBy = userId, CreatedAt = DateTime.UtcNow
        });
        return token;
    }

    public async Task SetIntakeActiveAsync(Guid id, bool active)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<IntakeFormDefinition>().Where(x => x.Id == id).Set(x => x.IsActive, active).Update();
        if (response.Models.Count == 0) throw new InvalidOperationException("Formulário não encontrado ou sem permissão.");
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
        requesterName = requesterName?.Trim() ?? string.Empty;
        requesterEmail = requesterEmail?.Trim();
        description = description?.Trim();
        if (requesterName.Length is < 2 or > 120) throw new InvalidOperationException("Informe seu nome.");
        if (description?.Length > 5000) throw new InvalidOperationException("Os detalhes devem ter no máximo 5.000 caracteres.");
        if (form.RequireEmail && string.IsNullOrWhiteSpace(requesterEmail)) throw new InvalidOperationException("Informe seu e-mail.");
        if (!string.IsNullOrWhiteSpace(requesterEmail))
        {
            try { _ = new MailAddress(requesterEmail); }
            catch (FormatException) { throw new InvalidOperationException("Informe um e-mail válido."); }
        }

        var client = _clientFactory.CreateServiceClient();
        var task = new PulseTask
        {
            Id = Guid.NewGuid(), BoardId = form.BoardId, Title = title.Trim(), Description = description,
            Status = form.TargetStatus, Priority = form.DefaultPriority, CreatedBy = form.CreatedBy,
            AccountableOwnerId = form.CreatedBy, WorkflowState = "waiting_external", CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow, StatusUpdatedAt = DateTime.UtcNow,
            CustomFields = new() { ["requester_name"] = requesterName, ["requester_email"] = requesterEmail, ["intake_form_id"] = form.Id }
        };
        await client.From<PulseTask>().Insert(task);
        return task.Id;
    }

    public async Task AddApprovalStepAsync(Guid taskId, int sequence, Guid approverId)
    {
        if (taskId == Guid.Empty || approverId == Guid.Empty || sequence < 1) throw new InvalidOperationException("Etapa de aprovação inválida.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var task = await client.From<PulseTask>().Where(x => x.Id == taskId).Single()
            ?? throw new InvalidOperationException("Tarefa não encontrada ou sem permissão.");
        var approver = await client.From<Profile>().Where(x => x.Id == approverId).Single();
        if (approver is not { IsActive: true }) throw new InvalidOperationException("O aprovador selecionado não está ativo.");
        await client.From<TaskApprovalStep>().Insert(new TaskApprovalStep
        { TaskId = task.Id, Sequence = sequence, ApproverId = approverId, Status = "waiting", CreatedAt = DateTime.UtcNow });
        await client.Rpc("activate_task_approval_if_required", new { p_task_id = task.Id });
    }

    public async Task DecideApprovalAsync(Guid stepId, string decision, string? note) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).Rpc("decide_task_approval",
            new { p_step_id = stepId, p_decision = decision, p_note = note });

    public async Task DeleteApprovalStepAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var step = await client.From<TaskApprovalStep>().Where(x => x.Id == id).Single()
            ?? throw new InvalidOperationException("Etapa de aprovação não encontrada.");
        await client.From<TaskApprovalStep>().Where(x => x.Id == id).Delete();
        var remaining = await client.From<TaskApprovalStep>().Where(x => x.TaskId == step.TaskId).Get();
        if (remaining.Models.Any(x => x.Status is "waiting" or "pending"))
            await client.Rpc("activate_task_approval_if_required", new { p_task_id = step.TaskId });
        else
            await client.From<PulseTask>().Where(x => x.Id == step.TaskId).Set(x => x.WorkflowState, "inbox").Update();
    }

    public async Task AddDelegationAsync(Guid delegatorId, Guid substituteId, DateTime startsOn, DateTime endsOn, Guid userId)
    {
        if (delegatorId == substituteId || endsOn.Date < startsOn.Date) throw new InvalidOperationException("Período ou substituto inválido.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<ApprovalDelegation>().Insert(new ApprovalDelegation
        { DelegatorId = delegatorId, SubstituteId = substituteId, StartsOn = startsOn.Date, EndsOn = endsOn.Date, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
    }

    public async Task DeleteDelegationAsync(Guid id) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).From<ApprovalDelegation>().Where(x => x.Id == id).Delete();

    public async Task AddMirrorAsync(Guid sourceTaskId, Guid targetTaskId, string fieldName, Guid userId)
    {
        if (sourceTaskId == targetTaskId) throw new InvalidOperationException("Origem e destino devem ser diferentes.");
        var allowed = new[] { "status", "priority", "due_date", "assigned_to" };
        if (!allowed.Contains(fieldName)) throw new InvalidOperationException("Campo espelhado inválido.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var source = await client.From<PulseTask>().Where(x => x.Id == sourceTaskId).Single()
            ?? throw new InvalidOperationException("Tarefa de origem não encontrada.");
        var target = await client.From<PulseTask>().Where(x => x.Id == targetTaskId).Single()
            ?? throw new InvalidOperationException("Tarefa de destino não encontrada.");
        if (fieldName == "status" && source.BoardId != target.BoardId)
        {
            var sourceBoard = await client.From<Board>().Where(x => x.Id == source.BoardId).Single();
            var targetBoard = await client.From<Board>().Where(x => x.Id == target.BoardId).Single();
            var sourceStages = sourceBoard?.Settings.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var targetStages = targetBoard?.Settings.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (!sourceStages.SetEquals(targetStages))
                throw new InvalidOperationException("Para espelhar status, os dois Boards precisam possuir as mesmas etapas.");
        }
        await client.From<TaskFieldMirror>().Insert(new TaskFieldMirror
        { SourceTaskId = sourceTaskId, TargetTaskId = targetTaskId, FieldName = fieldName, CreatedBy = userId, CreatedAt = DateTime.UtcNow });
    }

    public async Task DeleteMirrorAsync(Guid id) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).From<TaskFieldMirror>().Where(x => x.Id == id).Delete();

    public async Task AddCrossProjectDependencyAsync(Guid taskId, Guid dependsOnTaskId)
    {
        if (taskId == dependsOnTaskId) throw new InvalidOperationException("Uma tarefa não pode depender dela mesma.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var task = await client.From<PulseTask>().Where(x => x.Id == taskId).Single()
            ?? throw new InvalidOperationException("Tarefa não encontrada.");
        var prerequisite = await client.From<PulseTask>().Where(x => x.Id == dependsOnTaskId).Single()
            ?? throw new InvalidOperationException("Pré-requisito não encontrado.");
        if (task.BoardId == prerequisite.BoardId) throw new InvalidOperationException("Use esta área somente para dependências entre Boards diferentes.");
        await client.From<TaskDependency>().Insert(new TaskDependency
        { TaskId = taskId, DependsOnTaskId = dependsOnTaskId, DependencyType = "finish_to_start", CreatedAt = DateTime.UtcNow });
    }

    public async Task DeleteCrossProjectDependencyAsync(Guid id) =>
        await (await _clientFactory.CreateForCurrentUserAsync()).From<TaskDependency>().Where(x => x.Id == id).Delete();

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
        string? statusColumn, string? priorityColumn, string? dueDateColumn, string? source, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 1_800_000) throw new InvalidOperationException("Conteúdo de importação inválido.");
        var rows = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(payload) ?? [];
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId).Single()
            ?? throw new InvalidOperationException("Board não encontrado ou sem permissão.");
        if (board.Settings.Count == 0) throw new InvalidOperationException("Configure ao menos uma etapa no Board antes de importar.");
        var count = 0;
        foreach (var row in rows.Take(500))
        {
            var title = Value(row, titleColumn);
            if (string.IsNullOrWhiteSpace(title)) continue;
            DateTime? due = DateTime.TryParse(Value(row, dueDateColumn), CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var parsed) ? parsed.Date : null;
            await _boardService.CreateTaskAsync(new PulseTask
            {
                BoardId = boardId, Title = title, Description = Value(row, descriptionColumn),
                Status = NormalizeImportedStatus(Value(row, statusColumn), board.Settings), Priority = NormalizeImportedPriority(Value(row, priorityColumn)),
                DueDate = due, CreatedBy = userId, AccountableOwnerId = userId, WorkflowState = "inbox",
                CustomFields = new() { ["import_source"] = string.IsNullOrWhiteSpace(source) ? "excel" : source.Trim().ToLowerInvariant() }
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
    private static string NormalizeImportedStatus(string value, IReadOnlyList<BoardColumnSetting> settings)
    {
        var normalized = NormalizeText(value);
        var exact = settings.FirstOrDefault(x => NormalizeText(x.Id) == normalized || NormalizeText(x.Title) == normalized);
        if (exact != null) return exact.Id;
        string[] aliases = normalized switch
        {
            "working on it" or "em andamento" or "em execucao" or "in-progress" => ["in-progress", "andamento", "execucao", "progresso"],
            "done" or "feito" or "concluido" or "concluida" => ["done", "feito", "concluido", "concluida"],
            "homologacao" or "teste" or "em teste" => ["homologation", "homologacao", "teste"],
            "agendado" or "planejado" or "todo" or "a fazer" => ["todo", "planejado", "agendado", "fazer"],
            _ => []
        };
        var semantic = settings.FirstOrDefault(column => aliases.Any(alias =>
            NormalizeText(column.Id).Contains(alias) || NormalizeText(column.Title).Contains(alias)));
        return semantic?.Id ?? settings[0].Id;
    }
    private static string NormalizeImportedPriority(string value) => value.Trim().ToLowerInvariant() switch
    { "critical" or "crítica" => "critical", "high" or "alta" => "high", "low" or "baixa" => "low", _ => "medium" };
    private static bool ValidToken(string value) => value.Length == 48 && value.All(Uri.IsHexDigit);
    private static bool IsHexColor(string value) => value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
    private static string NormalizeColumnId(string value, int index)
    {
        var id = new string(value.Trim().ToLowerInvariant().Select(x => char.IsLetterOrDigit(x) ? x : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(id) ? $"stage-{index + 1}" : id;
    }
    private static string NormalizeText(string value)
    {
        var decomposed = (value ?? string.Empty).Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(x => CharUnicodeInfo.GetUnicodeCategory(x) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }
}
