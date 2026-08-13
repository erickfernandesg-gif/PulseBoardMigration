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

    public async Task<DashboardViewModel> GetDashboardAsync(Guid currentUserId = default)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var boards = await client.From<Board>().Get();
        var tasks = await client.From<PulseTask>().Get();
        var comments = await client.From<TaskComment>().Get();
        var profiles = await client.From<Profile>().Get();

        return new DashboardViewModel
        {
            CurrentUserId = currentUserId,
            Boards = boards.Models.OrderByDescending(b => b.CreatedAt).ToList(),
            Tasks = tasks.Models.Where(x => x.ArchivedAt == null).ToList(),
            RecentComments = comments.Models.OrderByDescending(c => c.CreatedAt).Take(10).ToList(),
            Profiles = profiles.Models.ToList()
        };
    }

    public async Task<AdminViewModel> GetAdminAsync(Guid currentUserId)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var profiles = await client.From<Profile>().Get();
        var teams = await client.From<Team>().Get();
        var rates = await client.From<UserRate>().Get();
        var clients = await client.From<ClientAccount>().Get();
        var current = profiles.Models.FirstOrDefault(p => p.Id == currentUserId);

        return new AdminViewModel
        {
            Profiles = profiles.Models.OrderBy(p => p.FullName ?? p.Email).ToList(),
            Teams = teams.Models.OrderBy(t => t.Name).ToList(),
            Rates = rates.Models.ToList(),
            Clients = clients.Models.OrderBy(c => c.Name).ToList(),
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
        var response = await client.From<Profile>()
            .Where(p => p.Id == id)
            .Set(p => p.FullName!, fullName.Trim())
            .Update();
        return response.Models.Count > 0;
    }

    public async Task<List<AutomationRule>> GetAutomationsAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        var response = await client.From<AutomationRule>().Get();
        return response.Models.OrderByDescending(a => a.CreatedAt).ToList();
    }

    public async Task<AutomationRule?> SaveAutomationAsync(AutomationRule rule)
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        rule.Title = rule.Title.Trim();
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
}
#pragma warning restore CS8603
