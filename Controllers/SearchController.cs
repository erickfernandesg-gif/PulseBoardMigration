using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class SearchController : Controller
{
    private readonly EnterpriseService _service;
    public SearchController(EnterpriseService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index(string q)
    {
        var results = await _service.SearchAsync(q ?? string.Empty);
        return Json(new { items = results });
    }
}
