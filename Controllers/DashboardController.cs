using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class DashboardController : Controller
{
    public IActionResult Index() => RedirectToAction("Index", "Work");
}
