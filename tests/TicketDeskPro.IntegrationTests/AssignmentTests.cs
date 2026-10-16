using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketDeskPro.Infrastructure.Data;
using Xunit;

namespace TicketDeskPro.IntegrationTests;

/// <summary>
/// US-6.02 category-aware round-robin: the auto-assignee is the active agent
/// with the FEWEST open tickets in the ticket's category (ties → overall load
/// → seniority). Proven with a second agent so the pick is not a freebie.
/// </summary>
public class AssignmentTests : IClassFixture<TicketApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TicketApiFactory _factory;

    public AssignmentTests(TicketApiFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientForAsync(string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await _factory.TokenForAsync(email));
        return client;
    }

    private async Task<string> CreateTicketForCategoryAsync(HttpClient customer, string categoryId)
    {
        var response = await customer.PostAsJsonAsync("/api/v1/tickets", new
        {
            title = "Category assignment probe",
            description = "Used by AssignmentTests to observe the auto-assignee.",
            priority = "Low",
            categoryId,
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return doc.GetProperty("data").GetProperty("assignedToId").GetString()!;
    }

    [Fact]
    public async Task Auto_assignment_prefers_the_agent_with_fewest_open_tickets_in_the_category()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var categories = await db.Categories.Take(2).Select(c => c.Id.ToString()).ToListAsync();
        Assert.Equal(2, categories.Count); // the seed provides several categories
        var catB = categories[0];
        var catC = categories[1];

        var admin = await ClientForAsync("admin@ticketdesk.dev");
        var email = $"agent2-{Guid.NewGuid():N}@ticketdesk.test";
        var create = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email,
            password = "Passw0rd!x",
            firstName = "Second",
            lastName = "Agent",
            phone = (string?)null,
            roles = new[] { "Agent" },
        }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var david = (await db.Users.FirstAsync(u => u.Email == "agent@ticketdesk.dev")).Id;
        var agent2 = (await db.Users.FirstAsync(u => u.Email == email)).Id;
        var customer = await ClientForAsync("customer@ticketdesk.dev");

        // Ticket 1 in cat B → David Chen (both agents empty → seniority).
        Assert.Equal(david.ToString(), await CreateTicketForCategoryAsync(customer, catB));

        // Ticket 2 in cat C → agent2 (overall load 0 vs David's 1).
        Assert.Equal(agent2.ToString(), await CreateTicketForCategoryAsync(customer, catC));

        // Ticket 3 in cat B → agent2: overall load is now tied (1-1), so the OLD
        // overall-only rule would pick David by seniority. Category-aware rule
        // sees cat B load David 1 / agent2 0 and picks agent2.
        Assert.Equal(agent2.ToString(), await CreateTicketForCategoryAsync(customer, catB));
    }
}
