using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class AutomationsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
