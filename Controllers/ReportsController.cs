using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class ReportsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
