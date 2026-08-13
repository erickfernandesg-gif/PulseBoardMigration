using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using System.Security.Claims;

namespace PulseBoardMigration.Controllers;

[AllowAnonymous]
public class AuthController : Controller
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        try
        {
            var login = await _authService.LoginAsync(email, password);
            var session = login?.Session;
            if (session?.User != null && login != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.Name, session.User.Email ?? email),
                    new(ClaimTypes.Email, session.User.Email ?? email),
                    new(ClaimTypes.NameIdentifier, session.User.Id ?? string.Empty),
                    new(ClaimTypes.Role, login.Profile.Role),
                    new("team_id", login.Profile.TeamId?.ToString() ?? string.Empty)
                };

                var identity = new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults.AuthenticationScheme);
                var properties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
                };
                properties.StoreTokens(new[]
                {
                    new AuthenticationToken { Name = "access_token", Value = session.AccessToken ?? string.Empty },
                    new AuthenticationToken { Name = "refresh_token", Value = session.RefreshToken ?? string.Empty }
                });

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(identity),
                    properties);

                return Url.IsLocalUrl(returnUrl)
                    ? LocalRedirect(returnUrl!)
                    : RedirectToAction("Index", "Dashboard");
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "E-mail ou senha incorretos.");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await _authService.LogoutAsync();
        }
        finally
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
