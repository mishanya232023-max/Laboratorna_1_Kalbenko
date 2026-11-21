using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace SecureLab.Api.Tests;

public sealed class CsrfRegressionTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static async Task<string> TitleAsync(HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonObject>(url))!["title"]!.GetValue<string>();

    [Fact]
    public async Task REG_CSRF_01_WithoutToken_Rejected_StateUnchanged_ThenValidPairSucceeds()
    {
        using var client = await factory.LoginWithCsrfAsync("alice", Lab04Credentials.Password);
        var id = Guid.Empty;
        try
        {
            using var created = await client.PostAsJsonAsync("/api/incidents", new
            {
                title = "REG-CSRF " + Guid.NewGuid().ToString("N")[..8],
                description = "CSRF regression resource.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var body = await created.Content.ReadFromJsonAsync<JsonObject>();
            id = Guid.Parse(body!["id"]!.GetValue<string>());
            var url = $"/api/incidents/{id}";

            var before = await TitleAsync(client, url);

            // Негативна частина: cookie є, request token немає.
            client.DropAntiforgeryToken();
            using var rejected = await client.PutAsJsonAsync(url,
                new { title = "changed-by-csrf", description = "x" });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, await TitleAsync(client, url));          // стан не змінився

            // Позитивна частина: валідна пара токенів.
            await client.UseAntiforgeryTokenAsync();
            using var accepted = await client.PutAsJsonAsync(url,
                new { title = "changed-with-token", description = "updated description" });
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal("changed-with-token", await TitleAsync(client, url)); // саме очікувана зміна
        }
        finally
        {
            if (id != Guid.Empty)
            {
                await client.UseAntiforgeryTokenAsync();
                (await client.DeleteAsync($"/api/incidents/{id}")).Dispose();
            }
        }
    }
}