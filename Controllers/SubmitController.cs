using Microsoft.AspNetCore.Mvc;

namespace PulseBoardMigration.Controllers
{
    public class SubmitController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
