using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
