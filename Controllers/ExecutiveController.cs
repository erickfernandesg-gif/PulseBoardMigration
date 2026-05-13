using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class ExecutiveController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
