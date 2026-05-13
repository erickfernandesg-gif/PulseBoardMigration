using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class SettingsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
