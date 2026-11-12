using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SecureLab.Api.Tests;

public sealed class RegisterContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StrongPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    private static object NewUser(string suffix, string password) => new
    {
        userName = "reg" + suffix,
        email = "reg" + suffix + "@example.test",
        displayName = "Тест " + suffix,
        password
    };

    [Fact]
    public async Task T01_Register_Valid_Returns201_WithSafeBodyOnly()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register", NewUser(suffix, StrongPassword));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "displayName", "id", "userName" }, names);
    }

    [Fact]
    public async Task T02_Register_Duplicate_Returns409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var client = factory.CreateClient();

        using var first = await client.PostAsJsonAsync(
            "/api/auth/register", NewUser(suffix, StrongPassword));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await client.PostAsJsonAsync(
            "/api/auth/register", NewUser(suffix, StrongPassword));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.DoesNotContain("password", await second.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T02_Register_WeakPassword_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register", NewUser(suffix, "short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}