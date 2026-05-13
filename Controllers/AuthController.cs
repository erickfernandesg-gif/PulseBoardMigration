using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using PulseBoardMigration.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using System.Collections.Generic;

namespace PulseBoardMigration.Controllers
{
    public class AuthController : Controller
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        // 1. Mostra a tela visual (GET)
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // 2. Recebe os dados quando o usuário clica em "Entrar" (POST)
        [HttpPost]
        public async Task<IActionResult> Login(string email, string password)
        {
            try
            {
                // Tenta logar no Supabase
                var session = await _authService.LoginAsync(email, password);

                if (session?.User != null)
                {
                    // Se deu certo, criamos o "Crachá" (Cookie) do C#
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, session.User.Email),
                        new Claim(ClaimTypes.NameIdentifier, session.User.Id)
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                    await HttpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(claimsIdentity));

                    // Redireciona para a lista de Quadros
                    return RedirectToAction("Index", "Boards");
                }
            }
            catch
            {
                // Se der erro (senha errada), mostra a mensagem
                ViewBag.ErrorMessage = "E-mail ou senha incorretos.";
            }

            return View();
        }

        // 3. Faz o Logout
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await _authService.LogoutAsync();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}