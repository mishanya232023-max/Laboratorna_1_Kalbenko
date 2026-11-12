using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class AuthSecurityTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private HttpClient Anonymous() => factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static object NewIncident(string title) => new
    {
        title,
        description = "Тест захищеного створення інциденту.",
        severity = "Low",
        occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };

    private async Task<Guid> ReadOwnerAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var incident = await db.Incidents.AsNoTracking().SingleAsync(item => item.Title == title);
        return incident.OwnerUserId;
    }

    private async Task DeleteAsync(string title)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A01_Anonymous_Me_Returns401_NotRedirect()
    {
        using var client = Anonymous();
        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A01_Anonymous_Create_Returns401_NotRedirect()
    {
        using var client = Anonymous();
        using var response = await client.PostAsJsonAsync(
            "/api/incidents", NewIncident("A01-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(response.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task A02_Login_Then_Me_ReturnsCurrentPrincipalWithSafeFields()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword);
        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(DbSeeder.AliceId.ToString(), root.GetProperty("userId").GetString());
        Assert.Equal("alice", root.GetProperty("userName").GetString());
        var names = root.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "role", "userId", "userName" }, names);
    }

    [Fact]
    public async Task A03_ValidCookie_WithOtherUserHeader_DoesNotChangeMe()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("X-Demo-UserId", DbSeeder.BobId.ToString());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceId.ToString(), doc.RootElement.GetProperty("userId").GetString());
    }

    [Fact]
    public async Task A04_AuthenticatedCreate_WithoutOwner_UsesPrincipalAsOwner()
    {
        var title = "A04-" + Guid.NewGuid().ToString("N");
        using var client = await factory.LoginAsync("alice", StudyPassword);
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", NewIncident(title));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.AliceId, await ReadOwnerAsync(title));
        }
        finally { await DeleteAsync(title); }
    }

    [Fact]
    public async Task A05_BobCreate_WithForgedOwnerAlice_OwnerStaysBob()
    {
        var title = "A05-" + Guid.NewGuid().ToString("N");
        using var client = await factory.LoginAsync("bob", StudyPassword);
        var body = new
        {
            title,
            description = "Тест підробленого власника.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            ownerUserId = DbSeeder.AliceId
        };
        try
        {
            using var response = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(DbSeeder.BobId, await ReadOwnerAsync(title));
        }
        finally { await DeleteAsync(title); }
    }

    [Fact]
    public async Task A06_Logout_ThenProtectedRequest_Returns401()
    {
        using var client = await factory.LoginAsync("alice", StudyPassword);

        using var before = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private static async Task<string> StableShapeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string Read(string name) =>
            doc.RootElement.TryGetProperty(name, out var value) ? value.ToString() : "<absent>";
        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}"
             + $"|{Read("type")}|{Read("title")}|{Read("status")}|{Read("detail")}";
    }

    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_ReturnSameExternalResult()
    {
        using var client = Anonymous();
        using var unknown = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "nobody" + Guid.NewGuid().ToString("N")[..8],
            password = "Wrong-Pass-1!"
        });
        using var wrong = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "alice",
            password = "Wrong-Pass-1!"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await StableShapeAsync(unknown), await StableShapeAsync(wrong));
        Assert.False(unknown.Headers.Contains("Set-Cookie"));
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_OverHttps_SetsCookieWithSecurityFlags()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { userName = "alice", password = StudyPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        const string cookieName = ".AspNetCore.Identity.Application=";
        var matching = response.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(cookieName)).ToArray();
        Assert.True(matching.Length == 1);

        var attributes = matching[0].Split(';').Skip(1)
            .Select(part => part.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
        Assert.Contains("path=/", attributes);
    }

    [Fact]
    public async Task Login_FiveFailures_Returns429_AndRecoversAfterWindow()
    {
        var original = LoginThrottle.Window;
        LoginThrottle.Window = TimeSpan.FromSeconds(2);
        try
        {
            using var client = Anonymous();
            for (var i = 0; i < LoginThrottle.MaxFailures; i++)
            {
                using var failure = await client.PostAsJsonAsync(
                    "/api/auth/login", new { userName = "alice", password = "Wrong-Pass-1!" });
                Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
            }

            using var blocked = await client.PostAsJsonAsync(
                "/api/auth/login", new { userName = "alice", password = StudyPassword });
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            Assert.True(blocked.Headers.Contains("Retry-After"));
            Assert.False(blocked.Headers.Contains("Set-Cookie"));

            await Task.Delay(TimeSpan.FromSeconds(2.5));

            using var recovered = await client.PostAsJsonAsync(
                "/api/auth/login", new { userName = "alice", password = StudyPassword });
            Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
        }
        finally
        {
            LoginThrottle.Window = original;
        }
    }
}