using PulseBoardMigration.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

#pragma warning disable CS8603 // Postgrest Set<T?> expression trees report nullable false positives.
namespace PulseBoardMigration.Services;

public class WorkspaceService
{
    private readonly SupabaseClientFactory _clientFactory;
    private readonly IConfiguration _configuration;

    public WorkspaceService(
        SupabaseClientFactory clientFactory,
        IConfiguration configuration)
    {
        _clientFactory = clientFactory;
        _configuration = configuration;
    }

    public async Task<AdminViewModel> GetAdminAsync(Guid currentUserId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var profiles = await client.From<Profile>().Get();
        var teams = await client.From<Team>().Get();
        var rates = await client.From<UserRate>().Get();
        var clients = await client.From<ClientAccount>().Get();
        var contracts = await client.From<ClientContract>().Get();
        var invoices = await client.From<BillingInvoice>().Get();
        var current = profiles.Models.FirstOrDefault(p => p.Id == currentUserId);

        return new AdminViewModel
        {
            Profiles = profiles.Models.OrderBy(p => p.FullName ?? p.Email).ToList(),
            Teams = teams.Models.OrderBy(t => t.Name).ToList(),
            Rates = rates.Models.ToList(),
            Clients = clients.Models.OrderBy(c => c.Name).ToList(),
            TeamMemberCounts = profiles.Models.GroupBy(x => x.TeamId).Where(x => x.Key.HasValue)
                .ToDictionary(x => x.Key!.Value, x => x.Count()),
            ClientContractCounts = contracts.Models.GroupBy(x => x.ClientId).ToDictionary(x => x.Key, x => x.Count()),
            ClientInvoiceCounts = invoices.Models.GroupBy(x => x.ClientId).ToDictionary(x => x.Key, x => x.Count()),
            IsManager = current?.Role is "admin" or "manager"
        };
    }

    public async Task<Profile?> GetProfileAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        return await client.From<Profile>().Where(p => p.Id == id).Single();
    }

    public async Task<List<ActivityLog>> GetNotificationsAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<ActivityLog>().Get();
        return response.Models.OrderByDescending(x => x.CreatedAt).Take(30).ToList();
    }

    public async Task MarkNotificationsReadAsync(Guid userId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<Profile>()
            .Where(p => p.Id == userId)
            .Set(p => p.LastReadNotificationsAt, DateTime.UtcNow)
            .Update();
    }

    public async Task<bool> UpdateProfileAsync(Guid id, string fullName)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        return await client.Rpc<bool>("update_own_profile_name", new { p_full_name = fullName.Trim() });
    }

    public async Task<List<AutomationRule>> GetAutomationsAsync(Guid? boardId = null)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = boardId.HasValue
            ? await client.From<AutomationRule>().Where(a => a.BoardId == boardId.Value).Get()
            : await client.From<AutomationRule>().Get();
        return response.Models.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<AutomationEditorViewModel> GetAutomationEditorAsync(Guid? boardId)
    {
        if (!boardId.HasValue) throw new InvalidOperationException("Selecione um projeto para configurar automações.");
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var board = await client.From<Board>().Where(x => x.Id == boardId.Value).Single();
        if (board == null) throw new InvalidOperationException("Projeto não encontrado ou sem permissão.");
        var profiles = await client.From<Profile>().Where(x => x.IsActive == true).Get();
        return new AutomationEditorViewModel
        {
            BoardId = boardId,
            Board = board,
            Rules = await GetAutomationsAsync(boardId),
            Profiles = profiles.Models.OrderBy(x => x.FullName ?? x.Email).ToList()
        };
    }

    public async Task<AutomationRule?> SaveAutomationAsync(AutomationRule rule)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        rule.Title = rule.Title?.Trim() ?? string.Empty;
        if (rule.Title.Length is < 1 or > 200) throw new InvalidOperationException("Informe um nome válido para a automação.");
        if (!rule.BoardId.HasValue) throw new InvalidOperationException("As automações devem pertencer a um projeto.");
        var triggerTypes = new[] { "status_change", "priority_change", "assignment_change" };
        var actionTypes = new[] { "notify_manager", "assign_user", "move_status", "set_priority", "set_due_days" };
        if (!triggerTypes.Contains(rule.TriggerType)) throw new InvalidOperationException("Gatilho de automação inválido.");
        if (rule.ActionType == "assign_auto") rule.ActionType = "assign_user";
        if (!actionTypes.Contains(rule.ActionType)) throw new InvalidOperationException("Ação de automação inválida.");

        var board = await client.From<Board>().Where(x => x.Id == rule.BoardId.Value).Single()
            ?? throw new InvalidOperationException("Projeto não encontrado ou sem permissão.");
        if (rule.TriggerType == "status_change" && board.Settings.All(x => x.Id != rule.TriggerValue))
            throw new InvalidOperationException("A etapa usada no gatilho não existe neste Board.");
        if (rule.TriggerType == "priority_change" && rule.TriggerValue is not ("low" or "medium" or "high" or "critical"))
            throw new InvalidOperationException("Selecione uma prioridade válida para o gatilho.");
        if (rule.TriggerType == "assignment_change") rule.TriggerValue = "any";
        if (rule.ActionType == "move_status" && board.Settings.All(x => x.Id != rule.ActionPayload))
            throw new InvalidOperationException("Selecione uma etapa válida para a ação.");
        if (rule.ActionType == "set_priority" && rule.ActionPayload is not ("low" or "medium" or "high" or "critical"))
            throw new InvalidOperationException("Prioridade da automação inválida.");
        if (rule.ActionType == "set_due_days" && (!int.TryParse(rule.ActionPayload, out var days) || days is < 0 or > 3650))
            throw new InvalidOperationException("O prazo deve ser informado em dias, entre 0 e 3650.");
        if (rule.ActionType == "assign_user")
        {
            if (!Guid.TryParse(rule.ActionPayload, out var assignedId)) throw new InvalidOperationException("Selecione um usuário válido.");
            var assigned = await client.From<Profile>().Where(x => x.Id == assignedId).Single();
            if (assigned is not { IsActive: true }) throw new InvalidOperationException("O usuário da automação não está ativo.");
        }
        if (rule.ActionType == "notify_manager") rule.ActionPayload = null;
        if (rule.Id == Guid.Empty)
        {
            rule.CreatedAt = DateTime.UtcNow;
            var inserted = await client.From<AutomationRule>().Insert(rule);
            return inserted.Models.FirstOrDefault();
        }

        var updated = await client.From<AutomationRule>()
            .Where(a => a.Id == rule.Id)
            .Set(a => a.Title, rule.Title)
            .Set(a => a.TriggerType, rule.TriggerType)
            .Set(a => a.TriggerValue, rule.TriggerValue)
            .Set(a => a.ActionType, rule.ActionType)
            .Set(a => a.ActionPayload!, rule.ActionPayload)
            .Set(a => a.IsActive, rule.IsActive)
            .Set(a => a.BoardId, rule.BoardId)
            .Update();
        return updated.Models.FirstOrDefault();
    }

    public async Task<bool> ToggleAutomationAsync(Guid id, bool active)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<AutomationRule>()
            .Where(a => a.Id == id)
            .Set(a => a.IsActive, active)
            .Update();
        return response.Models.Count > 0;
    }

    public async Task DeleteAutomationAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.From<AutomationRule>().Where(a => a.Id == id).Delete();
    }

    public async Task<Team?> SaveTeamAsync(Team team)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        if (team.Id == Guid.Empty)
        {
            team.CreatedAt = DateTime.UtcNow;
            var inserted = await client.From<Team>().Insert(team);
            return inserted.Models.FirstOrDefault();
        }

        var updated = await client.From<Team>()
            .Where(t => t.Id == team.Id)
            .Set(t => t.Name, team.Name.Trim())
            .Set(t => t.Description!, team.Description?.Trim())
            .Update();
        return updated.Models.FirstOrDefault();
    }

    public async Task DeleteTeamAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var profiles = await client.From<Profile>().Where(x => x.TeamId == id).Get();
        if (profiles.Models.Any())
            throw new InvalidOperationException("Esta equipe possui pessoas vinculadas. Reatribua-as antes de excluir a equipe.");
        await client.From<Team>().Where(t => t.Id == id).Delete();
    }

    public async Task<ClientAccount?> SaveClientAsync(ClientAccount account)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        if (account.Id == Guid.Empty)
        {
            account.CreatedAt = DateTime.UtcNow;
            var inserted = await client.From<ClientAccount>().Insert(account);
            return inserted.Models.FirstOrDefault();
        }

        var updated = await client.From<ClientAccount>()
            .Where(c => c.Id == account.Id)
            .Set(c => c.Name, account.Name.Trim())
            .Set(c => c.Email!, account.Email?.Trim())
            .Set(c => c.Phone!, account.Phone?.Trim())
            .Update();
        return updated.Models.FirstOrDefault();
    }

    public async Task DeleteClientAsync(Guid id)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var contracts = await client.From<ClientContract>().Where(x => x.ClientId == id).Get();
        var invoices = await client.From<BillingInvoice>().Where(x => x.ClientId == id).Get();
        if (invoices.Models.Any())
            throw new InvalidOperationException("Este cliente possui faturas. Preserve o cadastro para manter o histórico financeiro.");
        if (contracts.Models.Any())
            throw new InvalidOperationException("Este cliente possui contratos. Exclua ou encerre os contratos antes de excluir o cliente.");
        await client.From<ClientAccount>().Where(c => c.Id == id).Delete();
    }

    public async Task<bool> UpdateUserAsync(
        Guid id,
        string fullName,
        string role,
        Guid? teamId,
        decimal hourlyRate)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        ValidatePersonInput(fullName, role, hourlyRate);
        var profiles = await client.From<Profile>().Get();
        var target = profiles.Models.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Pessoa não encontrada.");
        if (teamId.HasValue)
        {
            var team = await client.From<Team>().Where(x => x.Id == teamId.Value).Single();
            if (team == null) throw new InvalidOperationException("Equipe não encontrada.");
        }
        if (target.IsActive && target.Role == "admin" && role != "admin" &&
            profiles.Models.Count(x => x.IsActive && x.Role == "admin") <= 1)
            throw new InvalidOperationException("Mantenha ao menos um administrador ativo na organização.");
        var profile = await client.From<Profile>()
            .Where(p => p.Id == id)
            .Set(p => p.FullName!, fullName.Trim())
            .Set(p => p.Role, role)
            .Set(p => p.TeamId, teamId)
            .Update();

        var currentRate = await client.From<UserRate>().Where(r => r.UserId == id).Single();
        if (currentRate == null)
        {
            await client.From<UserRate>().Insert(new UserRate
            {
                UserId = id,
                HourlyRate = Math.Max(0, hourlyRate),
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            await client.From<UserRate>()
                .Where(r => r.UserId == id)
                .Set(r => r.HourlyRate, Math.Max(0, hourlyRate))
                .Update();
        }

        return profile.Models.Count > 0;
    }

    public async Task<Guid> CreateEmployeeAsync(
        string email,
        string password,
        string fullName,
        string role,
        Guid? teamId,
        decimal hourlyRate)
    {
        ValidatePersonInput(fullName, role, hourlyRate);
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || password.Length < 8)
            throw new InvalidOperationException("Informe e-mail válido e senha provisória com ao menos 8 caracteres.");
        var url = RequiredSetting("Supabase:Url");
        var serviceKey = ServiceRoleSetting();
        using var http = CreateAdminHttpClient(serviceKey);
        var response = await http.PostAsJsonAsync($"{url.TrimEnd('/')}/auth/v1/admin/users", new
        {
            email = email.Trim(),
            password,
            email_confirm = true,
            user_metadata = new { full_name = fullName.Trim() }
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(payload);
        var id = json.RootElement.GetProperty("id").GetGuid();

        var service = _clientFactory.CreateServiceClient();
        await service.From<Profile>()
            .Where(p => p.Id == id)
            .Set(p => p.FullName!, fullName.Trim())
            .Set(p => p.Role, role)
            .Set(p => p.TeamId, teamId)
            .Update();
        await service.From<UserRate>().Insert(new UserRate
        {
            UserId = id,
            HourlyRate = Math.Max(0, hourlyRate),
            UpdatedAt = DateTime.UtcNow
        });
        return id;
    }

    public async Task DeactivateEmployeeAsync(Guid userId, Guid deactivatedBy)
    {
        var service = _clientFactory.CreateServiceClient();
        var profiles = await service.From<Profile>().Get();
        var target = profiles.Models.FirstOrDefault(x => x.Id == userId)
            ?? throw new InvalidOperationException("Pessoa não encontrada.");
        if (target.Role == "admin" && target.IsActive && profiles.Models.Count(x => x.IsActive && x.Role == "admin") <= 1)
            throw new InvalidOperationException("Não é possível desativar o último administrador ativo.");
        var tasks = await service.From<PulseTask>().Get();
        if (tasks.Models.Any(x => x.AssignedTo == userId && x.ArchivedAt == null && x.Status != "done"))
            throw new InvalidOperationException("Reatribua as tarefas abertas desta pessoa antes de desativá-la.");
        await service.From<Profile>()
            .Where(profile => profile.Id == userId)
            .Set(profile => profile.IsActive, false)
            .Set(profile => profile.DeactivatedAt, DateTime.UtcNow)
            .Set(profile => profile.DeactivatedBy, deactivatedBy)
            .Update();

        var url = RequiredSetting("Supabase:Url");
        var serviceKey = ServiceRoleSetting();
        using var http = CreateAdminHttpClient(serviceKey);
        var response = await http.PutAsJsonAsync($"{url.TrimEnd('/')}/auth/v1/admin/users/{userId}", new
        {
            ban_duration = "876000h"
        });
        response.EnsureSuccessStatusCode();
    }

    public async Task ReactivateEmployeeAsync(Guid userId)
    {
        var service = _clientFactory.CreateServiceClient();
        await service.From<Profile>()
            .Where(profile => profile.Id == userId)
            .Set(profile => profile.IsActive, true)
            .Set(profile => profile.DeactivatedAt, null)
            .Set(profile => profile.DeactivatedBy, null)
            .Update();

        var url = RequiredSetting("Supabase:Url");
        var serviceKey = ServiceRoleSetting();
        using var http = CreateAdminHttpClient(serviceKey);
        var response = await http.PutAsJsonAsync($"{url.TrimEnd('/')}/auth/v1/admin/users/{userId}", new
        {
            ban_duration = "none"
        });
        response.EnsureSuccessStatusCode();
    }

    private string RequiredSetting(string key)
    {
        return _configuration[key] ??
            throw new InvalidOperationException($"{key} não está configurada.");
    }

    private string ServiceRoleSetting()
    {
        return _configuration["Supabase:ServiceRoleKey"]
            ?? _configuration["Supabase:Key"]
            ?? throw new InvalidOperationException("Supabase:ServiceRoleKey não está configurada.");
    }

    private static HttpClient CreateAdminHttpClient(string serviceKey)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("apikey", serviceKey);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", serviceKey);
        return client;
    }

    private static void ValidatePersonInput(string fullName, string role, decimal hourlyRate)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length is < 2 or > 160)
            throw new InvalidOperationException("Informe um nome entre 2 e 160 caracteres.");
        if (role is not ("user" or "manager" or "admin"))
            throw new InvalidOperationException("Função inválida.");
        if (hourlyRate is < 0 or > 1_000_000)
            throw new InvalidOperationException("Informe um custo por hora válido.");
    }
}
#pragma warning restore CS8603
