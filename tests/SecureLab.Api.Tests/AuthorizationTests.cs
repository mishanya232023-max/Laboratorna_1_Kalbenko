using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using SecureLab.Api.Data;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Tests;

internal static class Lab04Credentials
{
    public static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
}

// Сценарії A-01 ... A-12 (рівень «Добре»). Кожен користувач має власний HttpClient із власним cookie jar.
public sealed class AuthorizationTests(SecureLabApiFactory factory) : IClassFixture<SecureLabApiFactory>
{
    private Task<HttpClient> LoginAsync(string userName) =>
        factory.LoginWithCsrfAsync(userName, Lab04Credentials.Password);

    private static string Url(Guid id) => $"/api/incidents/{id}";

    private static string ReadStatus(JsonObject details) => details["status"]!.GetValue<string>();

    // Позитивні та змінювальні тести працюють зі своїм інцидентом (завжди status New).
    private static async Task<Guid> CreateIncidentAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = "Lab 4 test " + Guid.NewGuid().ToString("N")[..8],
            description = "Synthetic incident created by an authorization test.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        Assert.True(response.IsSuccessStatusCode, $"Create failed: {(int)response.StatusCode}");
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        return Guid.Parse(body!["id"]!.GetValue<string>());
    }

    private static async Task<JsonObject> GetDetailsAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Url(id));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    [Fact]
    public async Task A01_Anonymous_ProtectedRoutes_Return401_NotRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        foreach (var path in new[]
                 {
                     "/api/incidents",
                     Url(DbSeeder.AliceIncidentId),
                     $"/api/incident-comments/{DbSeeder.AliceCommentId}"
                 })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }
    }

    [Fact]
    public async Task A02_Alice_ReadsOwnIncident_Returns200()
    {
        using var alice = await LoginAsync("alice");

        var details = await GetDetailsAsync(alice, DbSeeder.AliceIncidentId);

        Assert.Equal(DbSeeder.AliceIncidentId, Guid.Parse(details["id"]!.GetValue<string>()));
        Assert.Equal("Аліса Коваль", details["ownerDisplayName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A03_Alice_ReadsBobIncident_Returns404_WithoutBobData()
    {
        using var alice = await LoginAsync("alice");

        using var response = await alice.GetAsync(Url(DbSeeder.BobIncidentId));
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Невідома спроба входу", text);
        Assert.DoesNotContain("Боб Мельник", text);
    }

    [Fact]
    public async Task A03R_AnalystMorgan_ReadsAliceIncident_ButCannotMutateIt()
    {
        using var morgan = await LoginAsync("morgan");
        using var alice = await LoginAsync("alice");

        var details = await GetDetailsAsync(morgan, DbSeeder.AliceIncidentId);
        Assert.Equal("Аліса Коваль", details["ownerDisplayName"]!.GetValue<string>());

        using var update = await morgan.PutAsJsonAsync(
            Url(DbSeeder.AliceIncidentId),
            new { title = "Changed by analyst", description = "Changed by analyst" });
        using var delete = await morgan.DeleteAsync(Url(DbSeeder.AliceIncidentId));

        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);

        var after = await GetDetailsAsync(alice, DbSeeder.AliceIncidentId);
        Assert.Equal("Підозрілий лист із вкладенням", after["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task A04_Alice_UpdateOrDeleteBobIncident_Returns404_AndBobStateUnchanged()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");

        using var update = await alice.PutAsJsonAsync(
            Url(DbSeeder.BobIncidentId),
            new { title = "Hijacked", description = "Hijacked" });
        using var delete = await alice.DeleteAsync(Url(DbSeeder.BobIncidentId));

        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        var after = await GetDetailsAsync(bob, DbSeeder.BobIncidentId);
        Assert.Equal("Невідома спроба входу", after["title"]!.GetValue<string>());
    }

    [Fact]
    public async Task A04_Owner_DeletesOwnNewIncident_Returns204_ThenNotFound()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateIncidentAsync(alice);

        using var delete = await alice.DeleteAsync(Url(id));
        using var after = await alice.GetAsync(Url(id));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task A05_List_ContainsOnlyAllowedSet_PerRole()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        using var morgan = await LoginAsync("morgan");

        var aliceList = await alice.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");
        var bobList = await bob.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");
        var morganList = await morgan.GetFromJsonAsync<List<IncidentListItemResponse>>("/api/incidents");

        Assert.Contains(aliceList!, item => item.Id == DbSeeder.AliceIncidentId);
        Assert.DoesNotContain(aliceList!, item => item.Id == DbSeeder.BobIncidentId);

        Assert.Contains(bobList!, item => item.Id == DbSeeder.BobIncidentId);
        Assert.DoesNotContain(bobList!, item => item.Id == DbSeeder.AliceIncidentId);

        Assert.Contains(morganList!, item => item.Id == DbSeeder.AliceIncidentId);
        Assert.Contains(morganList!, item => item.Id == DbSeeder.BobIncidentId);
    }

    [Fact]
    public async Task A06_Reporter_Triage_Returns403_AndStatusUnchanged()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateIncidentAsync(alice);

        using var response = await alice.PostAsync($"/api/incidents/{id}/triage", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await GetDetailsAsync(alice, id);
        Assert.Equal("New", ReadStatus(after));
    }

    [Fact]
    public async Task A07_Analyst_Triage_NewIncident_Returns204_AndStatusTriaged()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");
        var id = await CreateIncidentAsync(alice);

        using var response = await morgan.PostAsync($"/api/incidents/{id}/triage", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await GetDetailsAsync(alice, id);
        Assert.Equal("Triaged", ReadStatus(after));

        // Повторний перехід недозволений: статус уже не New.
        using var second = await morgan.PostAsync($"/api/incidents/{id}/triage", content: null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task A07_OtherReporter_Triage_Returns404_AndStatusUnchanged()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateIncidentAsync(alice);

        using var response = await bob.PostAsync($"/api/incidents/{id}/triage", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = await GetDetailsAsync(alice, id);
        Assert.Equal("New", ReadStatus(after));
    }

    [Fact]
    public async Task A08a_Update_ForgedOwnerRoleStatus_AreIgnored()
    {
        using var alice = await LoginAsync("alice");
        using var bob = await LoginAsync("bob");
        var id = await CreateIncidentAsync(alice);

        using var response = await alice.PutAsJsonAsync(Url(id), new
        {
            title = "Updated by owner",
            description = "Updated description",
            ownerUserId = DbSeeder.BobId,
            role = "Administrator",
            status = "Closed",
            isTransitionAllowed = true
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await GetDetailsAsync(alice, id);
        Assert.Equal("Updated by owner", after["title"]!.GetValue<string>());
        Assert.Equal("Аліса Коваль", after["ownerDisplayName"]!.GetValue<string>());
        Assert.Equal("New", ReadStatus(after));

        using var asBob = await bob.GetAsync(Url(id));
        Assert.Equal(HttpStatusCode.NotFound, asBob.StatusCode);
    }

    [Fact]
    public async Task A09_DeniedPrivilegedAction_WritesSafeAuditEvent()
    {
        using var alice = await LoginAsync("alice");
        var id = await CreateIncidentAsync(alice);
        factory.AuditEvents.Clear();

        using var response = await alice.PostAsync($"/api/incidents/{id}/triage", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var auditEvent = Assert.Single(
            factory.AuditEvents.ToArray(),
            item => item.Contains("authorization.denied", StringComparison.Ordinal));
        Assert.Contains(DbSeeder.AliceId.ToString(), auditEvent);
        Assert.Contains("incident.triage", auditEvent);
        Assert.Contains(id.ToString(), auditEvent);
        Assert.Contains("traceId=", auditEvent);
        Assert.DoesNotContain(Lab04Credentials.Password, auditEvent);
        Assert.DoesNotContain("cookie", auditEvent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A10_ForeignAndUnknownId_ReturnIndistinguishable404()
    {
        using var alice = await LoginAsync("alice");

        using var foreign = await alice.GetAsync(Url(DbSeeder.BobIncidentId));
        using var unknown = await alice.GetAsync(Url(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(unknown.StatusCode, foreign.StatusCode);
        Assert.Equal(
            unknown.Content.Headers.ContentType?.ToString(),
            foreign.Content.Headers.ContentType?.ToString());

        var foreignBody = await foreign.Content.ReadFromJsonAsync<JsonObject>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<JsonObject>();
        foreignBody!.Remove("traceId");
        unknownBody!.Remove("traceId");
        foreignBody.Remove("requestId");
        unknownBody.Remove("requestId");
        Assert.True(JsonNode.DeepEquals(unknownBody, foreignBody));
    }

    [Fact]
    public async Task A12_ChildComment_FollowsParentAndInternalPolicy()
    {
        using var alice = await LoginAsync("alice");
        using var morgan = await LoginAsync("morgan");

        // Коментар Morgan до інциденту Bob: Alice його не бачить.
        using var foreignComment = await alice.GetAsync($"/api/incident-comments/{DbSeeder.BobCommentId}");
        Assert.Equal(HttpStatusCode.NotFound, foreignComment.StatusCode);

        // Публічний коментар до власного інциденту Alice доступний.
        using var ownComment = await alice.GetAsync($"/api/incident-comments/{DbSeeder.AliceCommentId}");
        Assert.Equal(HttpStatusCode.OK, ownComment.StatusCode);

        // Internal comment: власник-Reporter не бачить, Analyst бачить.
        using var internalAsAlice = await alice.GetAsync($"/api/incident-comments/{DbSeeder.InternalCommentId}");
        using var internalAsMorgan = await morgan.GetAsync($"/api/incident-comments/{DbSeeder.InternalCommentId}");
        Assert.Equal(HttpStatusCode.NotFound, internalAsAlice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, internalAsMorgan.StatusCode);

        // Те саме правило у details-проєкції.
        using var detailsAsAlice = await alice.GetAsync(Url(DbSeeder.AliceIncidentId));
        using var detailsAsMorgan = await morgan.GetAsync(Url(DbSeeder.AliceIncidentId));
        Assert.DoesNotContain("Внутрішня примітка", await detailsAsAlice.Content.ReadAsStringAsync());
        Assert.Contains("Внутрішня примітка", await detailsAsMorgan.Content.ReadAsStringAsync());
    }
}

