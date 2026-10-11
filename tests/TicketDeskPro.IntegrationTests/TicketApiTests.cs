using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketDeskPro.Infrastructure.Data;
using TicketDeskPro.Domain.Enums;
using Xunit;

namespace TicketDeskPro.IntegrationTests;

public class TicketApiTests : IClassFixture<TicketApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TicketApiFactory _factory;

    public TicketApiTests(TicketApiFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientForAsync(string email)
    {
        var client = _factory.CreateClient();
        var token = await _factory.TokenForAsync(email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<(JsonElement Data, string? ETag)> ReadAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return (doc.GetProperty("data"), response.Headers.ETag?.Tag?.Trim('"'));
    }

    private async Task<(string Id, string Number, string? ETag)> CreateTicketAsync(HttpClient client, string title = "Cannot login to dashboard", string priority = "High")
    {
        var categories = await GetCategoriesAsync(client);
        var response = await client.PostAsJsonAsync("/api/v1/tickets", new
        {
            title,
            description = "I receive a 500 error when logging in to the admin dashboard.",
            priority,
            categoryId = categories
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (data, etag) = await ReadAsync(response);
        return (data.GetProperty("id").GetString()!, data.GetProperty("ticketNumber").GetString()!, etag);
    }

    private async Task<string> GetCategoriesAsync(HttpClient client)
    {
        // Categories are seeded; read one via the db scope (no public endpoint yet — Phase 5 admin).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Categories.FirstAsync()).Id.ToString();
    }

    // ---------------- tests ----------------

    [Fact]
    public async Task Create_returns_201_with_TKT_number_and_etag()
    {
        var client = await ClientForAsync("customer@ticketdesk.dev");
        var (id, number, etag) = await CreateTicketAsync(client);

        Assert.Matches(@"^TKT-\d{4}$", number);   // BR-006
        Assert.False(string.IsNullOrEmpty(id));
        Assert.False(string.IsNullOrEmpty(etag));
    }

    [Fact]
    public async Task Customer_sees_only_own_tickets_agent_sees_all()
    {
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var agent = await ClientForAsync("agent@ticketdesk.dev");
        await CreateTicketAsync(customer, "My private problem");

        var customerList = await customer.GetFromJsonAsync<JsonElement>("/api/v1/tickets", Json);
        var agentList = await agent.GetFromJsonAsync<JsonElement>("/api/v1/tickets", Json);

        var customerItems = customerList.GetProperty("data").GetProperty("items");
        Assert.All(customerItems.EnumerateArray(), item =>
            Assert.Equal("Sarah Jenkins", item.GetProperty("createdByName").GetString())); // BR-022: own tickets only

        // agent sees at least what the customer sees, plus seeded-era tickets from other tests
        Assert.True(agentList.GetProperty("data").GetProperty("items").GetArrayLength() >= customerItems.GetArrayLength());
    }

    [Fact]
    public async Task Cursor_pagination_walks_pages_without_overlap()
    {
        var client = await ClientForAsync("agent@ticketdesk.dev");
        for (var i = 0; i < 3; i++)
            await CreateTicketAsync(client, $"Pagination ticket number {i}");

        var page1 = await client.GetFromJsonAsync<JsonElement>("/api/v1/tickets?limit=2", Json);
        var items1 = page1.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        var cursor = page1.GetProperty("data").GetProperty("nextCursor").GetString();
        Assert.True(page1.GetProperty("data").GetProperty("hasMore").GetBoolean());
        Assert.NotNull(cursor);

        var page2 = await client.GetFromJsonAsync<JsonElement>($"/api/v1/tickets?limit=2&cursor={Uri.EscapeDataString(cursor!)}", Json);
        var items2 = page2.GetProperty("data").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();

        Assert.Empty(items1.Intersect(items2)); // no overlap between pages
    }

    [Fact]
    public async Task Stale_If_Match_returns_409_concurrency_conflict()
    {
        var client = await ClientForAsync("agent@ticketdesk.dev");
        var (id, _, etag) = await CreateTicketAsync(client);

        // First update with the current ETag succeeds.
        var first = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{id}")
        {
            Content = JsonContent.Create(new { title = "Updated title one", description = "Still a long enough description.", priority = "High", categoryId = await GetCategoriesAsync(client) }, options: Json)
        };
        first.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{etag}\""));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(first)).StatusCode);

        // A mismatched ETag must conflict (CPL_004). The InMemory provider cannot simulate
        // xmin bumps (postgres-only), so we assert the mismatch path here; the true
        // stale-etag-after-update flow is covered by the live E2E run against PostgreSQL.
        var second = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/tickets/{id}")
        {
            Content = JsonContent.Create(new { title = "Updated title two", description = "Still a long enough description.", priority = "High", categoryId = await GetCategoriesAsync(client) }, options: Json)
        };
        second.Headers.IfMatch.Add(new EntityTagHeaderValue("\"999999\""));
        var response = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("CPL_004", body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_state_transition_returns_409()
    {
        var client = await ClientForAsync("agent@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(client);

        var response = await client.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "Resolved" }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("CPL_002", body.GetProperty("errors")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Open_to_InProgress_happy_path_then_customer_cannot_force_close()
    {
        var agent = await ClientForAsync("agent@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(customer); // customer owns it → may view

        var move = await agent.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "InProgress" }, Json);
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        // owner but customer role lacks tickets.delete → cannot force-close from any state
        var forceClose = await customer.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "Closed" }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, forceClose.StatusCode);
    }

    [Fact]
    public async Task Admin_force_closes_an_open_ticket()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(customer);

        var response = await admin.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "Closed", reason = "Duplicate" }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Internal_notes_are_agent_only_and_invisible_to_customers()
    {
        var agent = await ClientForAsync("agent@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(customer);

        // agent posts an internal note
        var note = await agent.PostAsJsonAsync($"/api/v1/tickets/{id}/messages", new { content = "Internal investigation note", isInternal = true }, Json);
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);

        // customer posting an internal note is forbidden
        var customerNote = await customer.PostAsJsonAsync($"/api/v1/tickets/{id}/messages", new { content = "sneaky", isInternal = true }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, customerNote.StatusCode);

        // the note is hidden from the customer's thread
        var customerThread = await customer.GetFromJsonAsync<JsonElement>($"/api/v1/tickets/{id}/messages", Json);
        Assert.Empty(customerThread.GetProperty("data").GetProperty("items").EnumerateArray().Where(m => m.GetProperty("isInternal").GetBoolean()));

        // but visible to the agent
        var agentThread = await agent.GetFromJsonAsync<JsonElement>($"/api/v1/tickets/{id}/messages", Json);
        Assert.Contains(agentThread.GetProperty("data").GetProperty("items").EnumerateArray(), m => m.GetProperty("isInternal").GetBoolean());
    }

    [Fact]
    public async Task Attachment_upload_download_delete_roundtrip()
    {
        var client = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(client);

        var form = new MultipartFormDataContent
        {
            { new ByteArrayContent("hello attachment"u8.ToArray()), "file", "notes.txt" }
        };
        var upload = await client.PostAsync($"/api/v1/tickets/{id}/attachments", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var (attachment, _) = await ReadAsync(upload);
        var attachmentId = attachment.GetProperty("id").GetString()!;

        var download = await client.GetAsync($"/api/v1/tickets/{id}/attachments/{attachmentId}/download");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("hello attachment", await download.Content.ReadAsStringAsync());

        var delete = await client.DeleteAsync($"/api/v1/tickets/{id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
    }

    [Fact]
    public async Task Attachment_delete_blocked_after_resolution()
    {
        var agent = await ClientForAsync("agent@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(customer);

        var form = new MultipartFormDataContent { { new ByteArrayContent("x"u8.ToArray()), "file", "shot.png" } };
        var upload = await customer.PostAsync($"/api/v1/tickets/{id}/attachments", form);
        var (attachment, _) = await ReadAsync(upload);
        var attachmentId = attachment.GetProperty("id").GetString()!;

        await agent.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "InProgress" }, Json);
        await agent.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "Resolved" }, Json);

        var delete = await customer.DeleteAsync($"/api/v1/tickets/{id}/attachments/{attachmentId}");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode); // BR-026
    }

    [Fact]
    public async Task Admin_soft_deletes_then_restores_a_ticket()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(customer);

        var delete = await admin.DeleteAsync($"/api/v1/tickets/{id}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        // gone for the customer
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/v1/tickets/{id}")).StatusCode);

        // soft-delete actually flagged the row (not removed) + audit captured the delete
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Tickets.IgnoreQueryFilters().SingleAsync(t => t.Id == Guid.Parse(id));
        Assert.True(row.IsDeleted);
        Assert.Contains(await db.AuditLogs.IgnoreQueryFilters().ToListAsync(), a => a.EntityType == "Ticket" && a.Action == "Delete");

        var restore = await admin.PostAsync($"/api/v1/tickets/{id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/api/v1/tickets/{id}")).StatusCode);
    }

    [Fact]
    public async Task Ticket_create_writes_an_audit_log_row()
    {
        var client = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(client);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.EntityType == "Ticket" && a.Action == "Create" && a.EntityId == id);

        Assert.NotNull(log);
        Assert.NotNull(log!.AfterJson); // BR-023 after-snapshot
        Assert.Contains("Cannot login", log!.AfterJson);
    }

    // ---------------- Phase 4: SLA policies, assignment, notifications ----------------

    [Fact]
    public async Task Admin_SLA_policy_crud_roundtrip_and_agent_forbidden()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var agent = await ClientForAsync("agent@ticketdesk.dev");

        // agent cannot manage policies
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsJsonAsync("/api/v1/admin/sla-policies",
            new { name = "Nope", priority = "Low", firstResponseMinutes = 1, resolutionMinutes = 2 }, Json)).StatusCode);

        // seeded policies own all four priorities — a duplicate priority must conflict
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/admin/sla-policies",
            new { name = "Dupe", priority = "Low", firstResponseMinutes = 100, resolutionMinutes = 200 }, Json)).StatusCode);

        // update the seeded Low policy
        var list1 = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sla-policies", Json);
        var lowId = list1.GetProperty("data").EnumerateArray()
            .First(p => p.GetProperty("priority").GetString() == "Low").GetProperty("id").GetString()!;

        var update = await admin.PutAsJsonAsync($"/api/v1/admin/sla-policies/{lowId}",
            new { name = "Updated Low SLA", firstResponseMinutes = 110, resolutionMinutes = 210, isActive = true }, Json);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var list2 = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sla-policies", Json);
        Assert.Contains(list2.GetProperty("data").EnumerateArray(), p => p.GetProperty("name").GetString() == "Updated Low SLA");

        // delete frees the priority, so the same create now succeeds
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/admin/sla-policies/{lowId}")).StatusCode);
        var create = await admin.PostAsJsonAsync("/api/v1/admin/sla-policies",
            new { name = "Fresh Low SLA", priority = "Low", firstResponseMinutes = 100, resolutionMinutes = 200 }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
    }

    [Fact]
    public async Task Ticket_is_auto_assigned_on_create_and_can_be_reassigned()
    {
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var agent = await ClientForAsync("agent@ticketdesk.dev");

        var categories = await GetCategoriesAsync(customer);
        var create = await customer.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = "Auto assign probe ticket",
            description = "A perfectly valid ticket description body.",
            priority = "High",
            categoryId = categories
        }, Json);
        var (created, etag) = (await ReadAsync(create));
        var id = created.GetProperty("id").GetString()!;
        Assert.NotNull(created.GetProperty("assignedToId").GetString()); // US-6.02 round-robin

        // reassign to admin (agents hold tickets.assign)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = (await db.Users.FirstAsync(u => u.Email == "admin@ticketdesk.dev")).Id;

        var assign = await agent.PostAsJsonAsync($"/api/v1/tickets/{id}/assign", new { assignedToId = adminId }, Json);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var (after, _) = await ReadAsync(assign);
        Assert.Equal(adminId.ToString(), after.GetProperty("assignedToId").GetString());

        // customer lacks tickets.assign
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync($"/api/v1/tickets/{id}/assign",
            new { assignedToId = adminId }, Json)).StatusCode);
    }

    [Fact]
    public async Task AwaitingCustomer_pauses_the_SLA_timer_and_customer_reply_resumes_it()
    {
        var agent = await ClientForAsync("agent@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, _, _) = await CreateTicketAsync(customer);

        await agent.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "InProgress" }, Json);
        await agent.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "AwaitingCustomer" }, Json);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var timer = await db.SlaTimers.FirstAsync(t => t.TicketId == Guid.Parse(id));
            Assert.NotNull(timer.PausedAt); // BR-031
        }

        var deadlineBefore = Guid.Parse(id);
        await customer.PatchAsJsonAsync($"/api/v1/tickets/{id}/status", new { status = "CustomerReplied" }, Json);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var timer = await db.SlaTimers.FirstAsync(t => t.TicketId == deadlineBefore);
            Assert.Null(timer.PausedAt); // BR-032 resumed
        }
    }

    [Fact]
    public async Task Ticket_creation_queues_customer_email_and_notifies_assigned_agent()
    {
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        var (id, number, _) = await CreateTicketAsync(customer);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // customer got a "we received your ticket" email through the outbox
        Assert.Contains(await db.EmailOutbox.IgnoreQueryFilters().ToListAsync(),
            o => o.ToEmail == "customer@ticketdesk.dev" && o.Subject.Contains(number));

        // the round-robin assignee got an in-app notification
        var ticket = await db.Tickets.IgnoreQueryFilters().FirstAsync(t => t.Id == Guid.Parse(id));
        Assert.NotNull(ticket.AssignedToId);
        Assert.Contains(await db.Notifications.IgnoreQueryFilters().ToListAsync(),
            n => n.UserId == ticket.AssignedToId && n.Type == Domain.Enums.NotificationType.TicketAssigned);
    }

    // ---------------- Phase 5: dashboard, admin, audit, preferences, health ----------------

    [Fact]
    public async Task Dashboard_kpis_returns_expected_shape_for_admin()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var kpis = await admin.GetFromJsonAsync<JsonElement>("/api/v1/dashboard/kpis", Json);
        var data = kpis.GetProperty("data");
        Assert.True(data.GetProperty("openByPriority").GetProperty("total").GetInt32() >= 0);
        Assert.True(data.GetProperty("slaComplianceRate7Days").GetDouble() >= 0);

        // the other dashboard views answer too
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/dashboard/ticket-trends?days=14")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/dashboard/category-distribution")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/dashboard/agent-performance")).StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_read_dashboard()
    {
        var customer = await ClientForAsync("customer@ticketdesk.dev");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/dashboard/kpis")).StatusCode);
    }

    [Fact]
    public async Task Admin_user_management_crud_with_superadmin_guards()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var superadmin = await ClientForAsync("superadmin@ticketdesk.dev");

        // create
        var create = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = "newguy@ticketdesk.dev", password = "StrongPass1!", firstName = "New", lastName = "Guy",
            phone = (string?)null, roles = new[] { "Agent" }
        }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var (user, _) = await ReadAsync(create);
        var userId = user.GetProperty("id").GetString()!;

        // duplicate email conflicts
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = "newguy@ticketdesk.dev", password = "StrongPass1!", firstName = "New", lastName = "Guy",
            phone = (string?)null, roles = new[] { "Agent" }
        }, Json)).StatusCode);

        // list + search
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/users?search=newguy", Json);
        Assert.Contains(list.GetProperty("data").GetProperty("items").EnumerateArray(),
            u => u.GetProperty("email").GetString() == "newguy@ticketdesk.dev");

        // deactivate
        var patch = await admin.PatchAsJsonAsync($"/api/v1/users/{userId}",
            new { firstName = "New", lastName = "Guy2", phone = (string?)null, isActive = false, roles = (IReadOnlyList<string>?)null }, Json);
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // agent cannot manage users
        var agent = await ClientForAsync("agent@ticketdesk.dev");
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PatchAsJsonAsync($"/api/v1/users/{userId}",
            new { firstName = "X", lastName = "Y", phone = (string?)null, isActive = true, roles = (IReadOnlyList<string>?)null }, Json)).StatusCode);

        // BR-38: deleting a SuperAdmin is blocked even for admin; superadmin CAN deactivate a superadmin? admin cannot deactivate superadmin either
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var saId = (await db.Users.FirstAsync(u => u.Email == "superadmin@ticketdesk.dev")).Id;
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/v1/users/{saId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PatchAsJsonAsync($"/api/v1/users/{saId}",
                new { firstName = "Super", lastName = "Admin", phone = (string?)null, isActive = false, roles = (IReadOnlyList<string>?)null }, Json)).StatusCode);
            // a SuperAdmin deactivating their OWN account is blocked by the self-guard
            Assert.Equal(HttpStatusCode.Conflict, (await superadmin.PatchAsJsonAsync($"/api/v1/users/{saId}",
                new { firstName = "Super", lastName = "Admin", phone = (string?)null, isActive = false, roles = (IReadOnlyList<string>?)null }, Json)).StatusCode);
        }

        // soft delete + restore
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/users/{userId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/users/{userId}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Superadmin_role_management_with_system_role_lock()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var superadmin = await ClientForAsync("superadmin@ticketdesk.dev");

        // admin lacks roles.manage
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/v1/admin/roles",
            new { name = "Nope", description = (string?)null, permissionIds = new[] { Guid.NewGuid() } }, Json)).StatusCode);

        // permissions catalog is readable
        var perms = await superadmin.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles/permissions", Json);
        Assert.True(perms.GetProperty("data").GetArrayLength() >= 24);

        var permId = perms.GetProperty("data")[0].GetProperty("id").GetString()!;

        // create custom role
        var create = await superadmin.PostAsJsonAsync("/api/v1/admin/roles",
            new { name = "Billing Agent", description = "Handles invoices", permissionIds = new[] { permId } }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var (role, _) = await ReadAsync(create);
        var roleId = role.GetProperty("id").GetString()!;

        // update + delete it
        Assert.Equal(HttpStatusCode.OK, (await superadmin.PutAsJsonAsync($"/api/v1/admin/roles/{roleId}",
            new { name = "Billing Specialist", description = "Handles invoices", permissionIds = new[] { permId } }, Json)).StatusCode);

        // system roles are locked
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agentRoleId = (await db.Roles.FirstAsync(r => r.Name == "Agent")).Id.ToString();
            Assert.Equal(HttpStatusCode.Conflict, (await superadmin.DeleteAsync($"/api/v1/admin/roles/{agentRoleId}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await superadmin.DeleteAsync($"/api/v1/admin/roles/{roleId}")).StatusCode);
    }

    [Fact]
    public async Task Category_and_tag_management_roundtrip()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");

        var cat = await admin.PostAsJsonAsync("/api/v1/categories",
            new { name = "Hardware", description = "Device issues" }, Json);
        Assert.Equal(HttpStatusCode.Created, cat.StatusCode);
        var (category, _) = await ReadAsync(cat);
        var catId = category.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/v1/categories/{catId}",
            new { name = "Hardware", description = "Device issues", isActive = false }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/categories/{catId}")).StatusCode);

        var tag = await admin.PostAsJsonAsync("/api/v1/tags", new { name = "vip-customer" }, Json);
        Assert.Equal(HttpStatusCode.Created, tag.StatusCode);
        var (createdTag, _) = await ReadAsync(tag);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/v1/tags/{createdTag.GetProperty("id").GetString()!}")).StatusCode);
    }

    [Fact]
    public async Task Audit_log_viewer_filters_and_requires_permission()
    {
        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var customer = await ClientForAsync("customer@ticketdesk.dev");

        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/audit-logs")).StatusCode);

        var logs = await admin.GetFromJsonAsync<JsonElement>("/api/v1/audit-logs?entityType=Ticket&limit=5", Json);
        var items = logs.GetProperty("data").GetProperty("items");
        Assert.All(items.EnumerateArray(), row => Assert.Equal("Ticket", row.GetProperty("entityType").GetString()));
        Assert.True(items.GetArrayLength() <= 5);
    }

    [Fact]
    public async Task Notification_preferences_roundtrip()
    {
        var agent = await ClientForAsync("agent@ticketdesk.dev");

        var current = await agent.GetFromJsonAsync<JsonElement>("/api/v1/notifications/preferences", Json);
        Assert.True(current.GetProperty("data").GetProperty("inAppEnabled").GetBoolean());

        var updated = await agent.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { emailOnTicketCreated = false, emailOnTicketAssigned = true, emailOnStatusChanged = true, emailOnSlaWarning = true, inAppEnabled = true }, Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var reread = await agent.GetFromJsonAsync<JsonElement>("/api/v1/notifications/preferences", Json);
        Assert.False(reread.GetProperty("data").GetProperty("emailOnTicketCreated").GetBoolean());
    }

    [Fact]
    public async Task Health_endpoints_behave()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/api/v1/healthz")).StatusCode);
        // InMemory + no Redis: readiness must report degraded (503)
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await anon.GetAsync("/api/v1/readyz")).StatusCode);
    }
}
