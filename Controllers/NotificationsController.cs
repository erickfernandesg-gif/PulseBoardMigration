using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PulseBoardMigration.Services;
using System.Security.Claims;
using System.Text.Json;

namespace PulseBoardMigration.Controllers;

[Authorize]
public class NotificationsController : Controller
{
    private readonly WorkManagementService _workService;

    public NotificationsController(WorkManagementService workService)
    {
        _workService = workService;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var items = await _workService.GetNotificationsAsync(userId);
        return Json(new
        {
            unreadCount = items.Count(item => item.ReadAt == null),
            items = items.Select(item => new
            {
                item.Id, item.Type, item.Title, item.Message, item.Priority,
                item.BoardId, item.TaskId, item.ActionUrl, item.ReadAt, item.CreatedAt
            })
        });
    }

    [HttpPost]
    public async Task<IActionResult> MarkRead(Guid? id)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        await _workService.MarkNotificationsReadAsync(userId, id);
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task Stream(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";
        var previousUnread = -1;
        while (!cancellationToken.IsCancellationRequested)
        {
            var items = await _workService.GetNotificationsAsync(userId, 20);
            var unread = items.Count(x => x.ReadAt == null);
            if (unread != previousUnread)
            {
                await Response.WriteAsync($"event: notification\ndata: {JsonSerializer.Serialize(new { unreadCount = unread })}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                previousUnread = unread;
            }
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
}
