using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class SecureLabApiFactory : WebApplicationFactory<Program>
{
    // Знімок audit-подій категорії SecureLab.Audit для перевірки A-09.
    public ConcurrentQueue<string> AuditEvents { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.AddProvider(new AuditCaptureProvider(AuditEvents)));
    }
}

internal sealed class AuditCaptureProvider(ConcurrentQueue<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName == "SecureLab.Audit" ? new AuditCaptureLogger(sink) : NullLogger.Instance;

    public void Dispose()
    {
    }
}

internal sealed class AuditCaptureLogger(ConcurrentQueue<string> sink) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) => sink.Enqueue(formatter(state, exception));
}
