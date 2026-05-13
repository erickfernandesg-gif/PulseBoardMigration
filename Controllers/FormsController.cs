using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class FormsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
