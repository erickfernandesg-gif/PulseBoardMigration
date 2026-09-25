using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Supabase;

namespace PulseBoardMigration.Services;

public class SupabaseClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

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
        var client = CreateAnonymousClient();
        var context = _httpContextAccessor.HttpContext;

        if (context?.User.Identity?.IsAuthenticated != true)
        {
            return client;
        }

        var currentUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var accessToken = await context.GetTokenAsync("access_token");
        var refreshToken = await context.GetTokenAsync("refresh_token");

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
        return new Client(url, key, new SupabaseOptions
        {
            AutoConnectRealtime = false,
            AutoRefreshToken = false
        });
    }
}
