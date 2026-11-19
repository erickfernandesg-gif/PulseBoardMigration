using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PulseBoardMigration.Tests;

// Opt-in: cria uma identidade e um projeto descartáveis, sem enviar e-mail.
// PULSEBOARD_RUN_LIVE_TESTS=1; PULSEBOARD_TEST_URL=http://127.0.0.1:5217
public sealed class LivePilotSmokeTests
{
    [LivePilotFact]
    public async Task AuthenticatedPagesMutationsUploadsAndRevocationWorkTogether()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("PulseBoardMigration-2e23e986-f52c-4ae3-952e-5236dd83454c")
            .AddEnvironmentVariables().Build();
        var apiUrl = config["Supabase:Url"] ?? throw new InvalidOperationException("Supabase não configurado.");
        using var api = new HttpClient { BaseAddress = new Uri(apiUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(30) };
        api.DefaultRequestHeaders.Add("apikey", config["Supabase:ServiceRoleKey"]);
        if (!config["Supabase:ServiceRoleKey"]!.StartsWith("sb_secret_", StringComparison.Ordinal))
            api.DefaultRequestHeaders.Authorization = new("Bearer", config["Supabase:ServiceRoleKey"]);
        using var web = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() })
        { BaseAddress = new Uri(Environment.GetEnvironmentVariable("PULSEBOARD_TEST_URL") ?? "http://127.0.0.1:5217"), Timeout = TimeSpan.FromSeconds(60) };
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"pilot-audit-{suffix}@example.invalid";
        var password = Guid.NewGuid().ToString("N") + "aA!9";
        var boardName = "AUDIT HTTP " + suffix;
        Guid? userId = null;
        Guid? boardId = null;
        var storage = new List<(string Bucket, string Path)>();
        try
        {
            using var created = await api.PostAsJsonAsync("auth/v1/admin/users", new
            { email, password, email_confirm = true, user_metadata = new { full_name = "Auditoria descartável" } });
            await RequireStatus(created, HttpStatusCode.OK);
            userId = (await ReadJson(created)).GetProperty("id").GetGuid();
            await RequireStatus(await api.PatchAsJsonAsync($"rest/v1/profiles?id=eq.{userId}", new { role = "admin" }), HttpStatusCode.NoContent);

            var loginHtml = await web.GetStringAsync("/Auth/Login");
            var token = Antiforgery(loginHtml);
            var login = await Post("/Auth/Login", new[] { ("email", email), ("password", password) });
            await RequireStatus(login, HttpStatusCode.Redirect);

            foreach (var path in new[] { "/Work", "/Boards", "/Planning", "/Management", "/Schedule", "/Reports", "/Reports/Export", "/Executive", "/Billing", "/Admin", "/Settings", "/Search?q=auditoria", "/Notifications/List" })
                await RequireStatus(await web.GetAsync(path), HttpStatusCode.OK);

            var boardsHtml = await web.GetStringAsync("/Boards");
            token = Antiforgery(boardsHtml);
            await RequireStatus(await Post("/Boards/Create", new[] { ("name", boardName), ("operationProfile", "delivery") }), HttpStatusCode.Redirect);
            using (var boards = await api.GetAsync($"rest/v1/boards?owner_id=eq.{userId}&select=id,name"))
            {
                await RequireStatus(boards, HttpStatusCode.OK);
                boardId = (await ReadJson(boards)).EnumerateArray().Single(x => x.GetProperty("name").GetString() == boardName).GetProperty("id").GetGuid();
            }
            var task = await Post("/Boards/CreateTask", new[] { ("boardId", boardId.ToString()!), ("title", "AUDIT HTTP tarefa"), ("columnId", "todo"), ("priority", "medium"), ("assignedTo", userId.ToString()!) });
            await RequireJsonSuccess(task);
            using var tasks = await api.GetAsync($"rest/v1/tasks?board_id=eq.{boardId}&select=id");
            var taskId = (await ReadJson(tasks))[0].GetProperty("id").GetGuid();
            await RequireStatus(await web.GetAsync($"/Boards/Details/{boardId}"), HttpStatusCode.OK);
            await RequireStatus(await web.GetAsync($"/BoardOperations/Index/{boardId}"), HttpStatusCode.OK);
            await RequireStatus(await web.GetAsync($"/Automations?boardId={boardId}"), HttpStatusCode.OK);

            await RequireJsonSuccess(await Post("/Boards/AddComment", new[] { ("taskId", taskId.ToString()), ("content", "AUDIT comentário") }));
            await RequireJsonSuccess(await Post("/Boards/AddChecklistItem", new[] { ("taskId", taskId.ToString()), ("title", "AUDIT checklist"), ("position", "0") }));

            using (var upload = new MultipartFormDataContent())
            {
                upload.Add(new StringContent(token), "__RequestVerificationToken");
                upload.Add(new StringContent(taskId.ToString()), "taskId");
                var bytes = new ByteArrayContent("Relatório de homologação fictício."u8.ToArray());
                bytes.Headers.ContentType = new("text/plain");
                upload.Add(bytes, "file", "homologacao.txt");
                var response = await web.PostAsync("/Boards/AddTaskFile", upload);
                await RequireJsonSuccess(response);
            }
            using (var files = await api.GetAsync($"rest/v1/task_files?task_id=eq.{taskId}&select=id,storage_path"))
            {
                var file = (await ReadJson(files))[0];
                storage.Add(("task-files", file.GetProperty("storage_path").GetString()!));
                var download = await web.GetAsync("/Boards/TaskFile/" + file.GetProperty("id").GetGuid());
                await RequireStatus(download, HttpStatusCode.OK);
                Assert.Contains("homologação", await download.Content.ReadAsStringAsync());
            }
            using (var upload = new MultipartFormDataContent())
            {
                upload.Add(new StringContent(token), "__RequestVerificationToken");
                upload.Add(new StringContent(taskId.ToString()), "taskId");
                upload.Add(new StringContent("AUDIT imagem"), "content");
                var png = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3x8AAAAASUVORK5CYII="));
                png.Headers.ContentType = new("image/png");
                upload.Add(png, "images", "evidencia.png");
                await RequireJsonSuccess(await web.PostAsync("/Boards/AddComment", upload));
            }
            using (var images = await api.GetAsync($"rest/v1/task_comment_attachments?task_id=eq.{taskId}&select=id,storage_path"))
            {
                var attachment = (await ReadJson(images))[0];
                storage.Add(("task-chat", attachment.GetProperty("storage_path").GetString()!));
                await RequireStatus(await web.GetAsync("/Boards/CommentAttachment/" + attachment.GetProperty("id").GetGuid()), HttpStatusCode.OK);
            }
            await RequireStatus(await web.GetAsync($"/Boards/Details/{boardId}"), HttpStatusCode.OK);

            token = Antiforgery(await web.GetStringAsync("/Settings"));
            await RequireStatus(await Post("/Settings/UpdateNotifications", new[] { ("inApp", "true"), ("dueReminders", "true"), ("budgetAlerts", "true"), ("mentionAlerts", "true"), ("inApp", "false"), ("dueReminders", "false"), ("budgetAlerts", "false"), ("mentionAlerts", "false") }), HttpStatusCode.Redirect);
            using (var preferences = await api.GetAsync($"rest/v1/notification_preferences?user_id=eq.{userId}"))
            {
                var preference = (await ReadJson(preferences))[0];
                Assert.True(preference.GetProperty("in_app").GetBoolean());
                Assert.True(preference.GetProperty("due_reminders").GetBoolean());
                Assert.True(preference.GetProperty("budget_alerts").GetBoolean());
                Assert.True(preference.GetProperty("mention_alerts").GetBoolean());
            }
            // O mesmo cookie perde os poderes administrativos imediatamente.
            await RequireStatus(await api.PatchAsJsonAsync($"rest/v1/profiles?id=eq.{userId}", new { role = "user" }), HttpStatusCode.NoContent);
            var forbidden = await web.GetAsync("/Admin");
            await RequireStatus(forbidden, HttpStatusCode.Redirect);
            Assert.Contains("AccessDenied", forbidden.Headers.Location!.ToString());
            await RequireStatus(await api.PatchAsJsonAsync($"rest/v1/profiles?id=eq.{userId}", new { is_active = false }), HttpStatusCode.NoContent);
            var disabled = await web.GetAsync("/Work");
            await RequireStatus(disabled, HttpStatusCode.Redirect);
            Assert.Contains("/Auth/Login", disabled.Headers.Location!.ToString());

            async Task<HttpResponseMessage> Post(string path, IEnumerable<(string Key, string Value)> values)
            {
                var fields = values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).ToList();
                fields.Add(new("__RequestVerificationToken", token));
                return await web.PostAsync(path, new FormUrlEncodedContent(fields));
            }
        }
        finally
        {
            // Limpa apenas IDs criados nesta execução, mesmo se uma verificação falhar.
            if (boardId.HasValue)
            {
                foreach (var table in new[] { "task_files", "task_comment_attachments" })
                {
                    using var objects = await api.GetAsync($"rest/v1/{table}?select=storage_path,tasks!inner(board_id)&tasks.board_id=eq.{boardId}");
                    if (objects.IsSuccessStatusCode)
                        foreach (var item in (await ReadJson(objects)).EnumerateArray())
                            storage.Add((table == "task_files" ? "task-files" : "task-chat", item.GetProperty("storage_path").GetString()!));
                }
                await RequireStatus(await api.DeleteAsync($"rest/v1/boards?id=eq.{boardId}"), HttpStatusCode.NoContent);
            }
            foreach (var bucket in storage.Distinct().GroupBy(x => x.Bucket))
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, "storage/v1/object/" + bucket.Key)
                { Content = JsonContent.Create(new { prefixes = bucket.Select(x => x.Path).ToArray() }) };
                await RequireStatus(await api.SendAsync(request), HttpStatusCode.OK);
            }
            if (userId.HasValue)
                await RequireStatus(await api.DeleteAsync($"auth/v1/admin/users/{userId}"), HttpStatusCode.OK);
        }
    }

    private static string Antiforgery(string html) => WebUtility.HtmlDecode(
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task RequireJsonSuccess(HttpResponseMessage response)
    {
        await RequireStatus(response, HttpStatusCode.OK);
        Assert.True((await ReadJson(response)).GetProperty("success").GetBoolean());
    }

    private static Task RequireStatus(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.True(response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: esperado {(int)expected}, recebido {(int)response.StatusCode}");
        return Task.CompletedTask;
    }

    private sealed class LivePilotFactAttribute : FactAttribute
    {
        public LivePilotFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("PULSEBOARD_RUN_LIVE_TESTS") != "1")
                Skip = "Integração real é opt-in; exige servidor local e segredos Supabase.";
        }
    }
}
