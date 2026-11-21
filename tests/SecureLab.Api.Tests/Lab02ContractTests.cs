using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class Lab02ContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");

    [Fact]
    public async Task T02_Post_WithInvalidSeverity_Returns400WithSeverityKey()
    {
        using var client = await factory.LoginWithCsrfAsync("alice", StudyPassword);
        var body = new
        {
            title = $"Invalid-{Guid.NewGuid():N}",
            description = "���� ���������� severity.",
            severity = "7",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        using var response = await client.PostAsJsonAsync("/api/incidents", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var errors = document.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("severity", out _));
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T03_Post_DuplicateActiveTitle_Returns409()
    {
        using var client = await factory.LoginWithCsrfAsync("alice", StudyPassword);
        var title = $"Regression-{Guid.NewGuid():N}";
        var body = new
        {
            title,
            description = "���� ����������� ��������.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        try
        {
            using var first = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);

            using var second = await client.PostAsJsonAsync("/api/incidents", body);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);

            var json = await second.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        }
        finally
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            await db.Incidents.Where(item => item.Title == title).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task S02_Search_ControlInput_ReturnsEmpty_AndUsbReturnsSingle()
    {
        using var client = factory.CreateClient();

        var attack = Uri.EscapeDataString("zz-no-match' OR TRUE -- ");
        using var attackResponse = await client.GetAsync("/api/incidents/search?q=" + attack);
        Assert.Equal(HttpStatusCode.OK, attackResponse.StatusCode);
        using var attackDoc = JsonDocument.Parse(await attackResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, attackDoc.RootElement.GetArrayLength());

        using var usbResponse = await client.GetAsync("/api/incidents/search?q=USB");
        Assert.Equal(HttpStatusCode.OK, usbResponse.StatusCode);
        using var usbDoc = JsonDocument.Parse(await usbResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, usbDoc.RootElement.GetArrayLength());
    }
}
