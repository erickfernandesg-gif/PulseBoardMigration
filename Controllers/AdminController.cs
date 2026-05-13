using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class AdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
