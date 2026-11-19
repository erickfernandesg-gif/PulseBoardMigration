using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Supabase;
using PulseBoardMigration.Models;

namespace PulseBoardMigration.Services;

public class SupabaseClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Task<Client>? _currentClientTask;

    public SupabaseClientFactory(
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public Client CreateAnonymousClient()
    {
        var url = _configuration["Supabase:Url"];
        var key = _configuration["Supabase:AnonKey"] ?? _configuration["Supabase:Key"];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Configure Supabase:Url e Supabase:AnonKey usando variáveis de ambiente ou User Secrets.");
        }

        return CreateClient(url, key);
    }

    public async Task<Client> CreateForCurrentUserAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true)
        {
            return CreateAnonymousClient();
        }
        if (_currentClientTask != null) return await _currentClientTask;
        var authentication = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (authentication.Properties == null) throw new UnauthorizedAccessException("Sessão inválida.");
        return await (_currentClientTask ??= CreateSessionClientAsync(context.User, authentication.Properties));
    }

    // Cookie validation and every service in the request share the same refreshed session.
    public virtual async Task<Profile> ValidateSessionAsync(ClaimsPrincipal principal, AuthenticationProperties properties)
    {
        var client = await (_currentClientTask ??= CreateSessionClientAsync(principal, properties));
        var id = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var profile = await client.From<Profile>().Where(item => item.Id == id).Single();
        if (profile is not { IsActive: true }) throw new UnauthorizedAccessException("Usuário desativado.");
        return profile;
    }

    private async Task<Client> CreateSessionClientAsync(ClaimsPrincipal principal, AuthenticationProperties properties)
    {
        var client = CreateAnonymousClient();
        var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessToken = properties.GetTokenValue("access_token");
        var refreshToken = properties.GetTokenValue("refresh_token");

        if (string.IsNullOrWhiteSpace(currentUserId) ||
            string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new UnauthorizedAccessException(
                "Sua sessão de acesso ao banco não está disponível. Saia e entre novamente.");
        }

        // Inicializa o cliente antes de aplicar a sessão para que o token seja
        // propagado ao PostgREST, que é a camada responsável por aplicar RLS.
        await client.InitializeAsync();
        await client.Auth.SetSession(accessToken, refreshToken, false);

        if (!string.Equals(client.Auth.CurrentUser?.Id, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "A sessão do sistema não corresponde à sessão de acesso ao banco. Saia e entre novamente.");
        }

        var session = client.Auth.CurrentSession
            ?? throw new UnauthorizedAccessException("Sessão expirada.");
        properties.UpdateTokenValue("access_token", session.AccessToken ?? accessToken);
        properties.UpdateTokenValue("refresh_token", session.RefreshToken ?? refreshToken);

        return client;
    }

    public Client CreateServiceClient()
    {
        var url = _configuration["Supabase:Url"];
        var serviceRoleKey = _configuration["Supabase:ServiceRoleKey"]
            ?? _configuration["Supabase:Key"];

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(serviceRoleKey))
        {
            throw new InvalidOperationException(
                "A operação administrativa exige Supabase:ServiceRoleKey configurada fora do código-fonte.");
        }

        return CreateClient(url, serviceRoleKey);
    }

    private static Client CreateClient(string url, string key)
    {
        var client = new Client(url, key, new SupabaseOptions
        {
            AutoConnectRealtime = false,
            AutoRefreshToken = false
        });
        if (key.StartsWith("sb_publishable_", StringComparison.Ordinal) || key.StartsWith("sb_secret_", StringComparison.Ordinal))
        {
            // This SDK predates opaque API keys. Keep them in apikey only;
            // authenticated user JWTs must still flow to every data request.
            var originalHeaders = client.Postgrest.GetHeaders!;
            Dictionary<string, string> Headers()
            {
                var headers = originalHeaders();
                if (headers.GetValueOrDefault("Authorization") == $"Bearer {key}")
                    headers.Remove("Authorization");
                return headers;
            }
            client.Auth.GetHeaders = Headers;
            client.Postgrest.GetHeaders = Headers;
            client.Storage.GetHeaders = Headers;
            client.Functions.GetHeaders = Headers;
        }
        return client;
    }
}
