namespace PulseBoardMigration.Services;

public sealed record AuthenticatedLogin(
    Supabase.Gotrue.Session Session,
    PulseBoardMigration.Models.Profile Profile);

public class AuthService
{
    private readonly SupabaseClientFactory _clientFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public AuthService(
        SupabaseClientFactory clientFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _clientFactory = clientFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<AuthenticatedLogin?> LoginAsync(string email, string password)
    {
        var client = _clientFactory.CreateAnonymousClient();
        await client.InitializeAsync();
        var session = await client.Auth.SignIn(email, password);
        if (session?.User?.Id == null || !Guid.TryParse(session.User.Id, out var userId)) return null;
        var profile = await client.From<PulseBoardMigration.Models.Profile>()
            .Where(item => item.Id == userId)
            .Single();
        if (profile == null || !profile.IsActive)
        {
            await client.Auth.SignOut();
            throw new UnauthorizedAccessException("Este usuário está desativado.");
        }
        return new AuthenticatedLogin(session, profile);
    }

    public async Task LogoutAsync()
    {
        var client = await _clientFactory.CreateForCurrentUserAsync();
        await client.Auth.SignOut();
    }

    public async Task RequestPasswordResetAsync(string email, string redirectTo)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(redirectTo))
        {
            throw new InvalidOperationException("Não foi possível iniciar a recuperação de senha.");
        }

        await SendAuthRequestAsync(HttpMethod.Post, "recover", new
        {
            email = email.Trim(),
            redirect_to = redirectTo
        });
    }

    public async Task ChangePasswordAsync(string email, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(currentPassword))
        {
            throw new InvalidOperationException("Informe a senha atual para continuar.");
        }

        var verifier = _clientFactory.CreateAnonymousClient();
        await verifier.InitializeAsync();

        Supabase.Gotrue.Session? verifiedSession;
        try
        {
            verifiedSession = await verifier.Auth.SignIn(email, currentPassword);
        }
        catch
        {
            throw new InvalidOperationException("A senha atual está incorreta.");
        }

        if (string.IsNullOrWhiteSpace(verifiedSession?.AccessToken))
        {
            throw new InvalidOperationException("A senha atual está incorreta.");
        }

        await UpdatePasswordAsync(verifiedSession.AccessToken, newPassword);
    }

    public Task ResetPasswordAsync(string recoveryAccessToken, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(recoveryAccessToken))
        {
            throw new InvalidOperationException("O link de recuperação é inválido ou expirou.");
        }

        return UpdatePasswordAsync(recoveryAccessToken, newPassword);
    }

    private Task UpdatePasswordAsync(string accessToken, string newPassword) =>
        SendAuthRequestAsync(HttpMethod.Put, "user", new { password = newPassword }, accessToken);

    private async Task SendAuthRequestAsync(HttpMethod method, string path, object body, string? accessToken = null)
    {
        var supabaseUrl = RequiredSetting("Supabase:Url").TrimEnd('/');
        var anonKey = RequiredSetting("Supabase:AnonKey", "Supabase:Key");
        using var request = new HttpRequestMessage(method, $"{supabaseUrl}/auth/v1/{path}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("apikey", anonKey);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var response = await _httpClientFactory.CreateClient().SendAsync(request);
        if (response.IsSuccessStatusCode) return;

        if (path == "user")
        {
            throw new InvalidOperationException("Não foi possível alterar a senha. O link pode ter expirado; solicite outro e tente novamente.");
        }

        throw new InvalidOperationException("Não foi possível enviar o link de recuperação. Tente novamente em alguns minutos.");
    }

    private string RequiredSetting(params string[] names)
    {
        foreach (var name in names)
        {
            var value = _configuration[name];
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }

        throw new InvalidOperationException("A autenticação por Supabase não está configurada corretamente.");
    }
}
