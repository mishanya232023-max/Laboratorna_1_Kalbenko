using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace SecureLab.Api.Tests;

public sealed class CsrfRegressionTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task REG_CSRF_01_WithoutToken_Rejected_StateUnchanged_ThenValidPairSucceeds()
    {
        using var client = await factory.LoginAsync("alice", Lab04Credentials.Password);
        var title = "csrf-" + Guid.NewGuid().ToString("N")[..8];
        var payload = new
        {
            title,
            description = "CSRF regression probe",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        var rejected = await client.PostAsJsonAsync("/api/incidents", payload);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var afterRejected = await client.GetStringAsync("/api/incidents/search?q=" + title);
        Assert.Equal("[]", afterRejected);

        await client.UseAntiforgeryTokenAsync("/api/csrf-token", "X-CSRF-TOKEN");
        var accepted = await client.PostAsJsonAsync("/api/incidents", payload);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var afterAccepted = await client.GetStringAsync("/api/incidents/search?q=" + title);
        Assert.Contains(title, afterAccepted);
    }
}