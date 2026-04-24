using System.Net;
using System.Text.Json;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Infrastructure.Persistence;

namespace Ringmaster.IntegrationTests;

public sealed class WebhookNotificationSinkTests : IDisposable
{
    private readonly TestHttpMessageHandler _handler;
    private readonly WebhookUrlValidator _validator;

    public WebhookNotificationSinkTests()
    {
        _handler = new TestHttpMessageHandler();
        _validator = new WebhookUrlValidator(new WebhookUrlSecurityPolicy
        {
            AllowLocalhost = true,
        });
    }

    public void Dispose()
    {
        _handler.Dispose();
    }

    [Fact]
    public async Task NotifyAsync_SendsCompactJsonPayload()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);
        WebhookNotificationConfig config = MinimalConfig();
        using WebhookNotificationSink sink = CreateSink(config);
        NotificationRecord notification = SampleNotification();

        await sink.NotifyAsync(notification, CancellationToken.None);

        Assert.Single(_handler.CapturedRequests);
        HttpRequestMessage request = _handler.CapturedRequests[0];
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Single(_handler.CapturedBodies);
        using JsonDocument doc = JsonDocument.Parse(_handler.CapturedBodies[0]);
        Assert.Equal("job.completed", doc.RootElement.GetProperty("eventType").GetString());
    }

    [Fact]
    public async Task NotifyAsync_RetriesOnFailure_AndEventuallySwallowsError()
    {
        int attempt = 0;
        _handler.ResponseFactory = _ =>
        {
            attempt++;
            return new HttpResponseMessage(attempt < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        };

        WebhookNotificationConfig config = MinimalConfig() with { MaxRetries = 2, RetryDelaySeconds = 0 };
        using WebhookNotificationSink sink = CreateSink(config);

        await sink.NotifyAsync(SampleNotification(), CancellationToken.None);
        Assert.Equal(3, _handler.CapturedRequests.Count);
    }

    [Fact]
    public async Task NotifyAsync_SwallowsErrorAfterAllRetriesExhausted()
    {
        _handler.ResponseFactory = _ => throw new HttpRequestException("Network error");
        WebhookNotificationConfig config = MinimalConfig() with { MaxRetries = 1, RetryDelaySeconds = 0 };
        using WebhookNotificationSink sink = CreateSink(config);

        // Should not throw despite network failure.
        await sink.NotifyAsync(SampleNotification(), CancellationToken.None);
        Assert.Equal(2, _handler.CapturedRequests.Count);
    }

    [Fact]
    public async Task NotifyAsync_RespectsEventTypeFilter()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);
        WebhookNotificationConfig config = MinimalConfig() with
        {
            AllowedEventTypes = ["job.completed"],
        };
        using WebhookNotificationSink sink = CreateSink(config);

        await sink.NotifyAsync(SampleNotification() with { EventType = "job.started" }, CancellationToken.None);
        await sink.NotifyAsync(SampleNotification() with { EventType = "job.completed" }, CancellationToken.None);

        Assert.Single(_handler.CapturedRequests);
    }

    [Fact]
    public async Task NotifyAsync_AddsBearerToken_WhenConfigured()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);
        const string envVarName = "RINGMASTER_TEST_WEBHOOK_SECRET";
        try
        {
            Environment.SetEnvironmentVariable(envVarName, "test-token-123");
            WebhookNotificationConfig config = MinimalConfig() with
            {
                Authentication = WebhookAuthType.BearerToken,
                SecretEnvironmentVariable = envVarName,
            };
            using WebhookNotificationSink sink = CreateSink(config);

            await sink.NotifyAsync(SampleNotification(), CancellationToken.None);

            string? auth = _handler.CapturedRequests[0].Headers.Authorization?.ToString();
            Assert.NotNull(auth);
            Assert.StartsWith("Bearer test-token-123", auth);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task NotifyAsync_AddsHmacSignature_WhenConfigured()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);
        const string envVarName = "RINGMASTER_TEST_WEBHOOK_HMAC";
        try
        {
            Environment.SetEnvironmentVariable(envVarName, "hmac-secret-456");
            WebhookNotificationConfig config = MinimalConfig() with
            {
                Authentication = WebhookAuthType.HmacSha256,
                SecretEnvironmentVariable = envVarName,
            };
            using WebhookNotificationSink sink = CreateSink(config);

            await sink.NotifyAsync(SampleNotification(), CancellationToken.None);

            IEnumerable<string>? signatures = _handler.CapturedRequests[0].Headers.GetValues("X-Webhook-Signature");
            Assert.NotNull(signatures);
            string signature = Assert.Single(signatures);
            Assert.StartsWith("sha256=", signature);
            Assert.Equal(71, signature.Length); // "sha256=" + 64 hex chars
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task NotifyAsync_NoAuth_WhenSecretMissing()
    {
        _handler.ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK);
        WebhookNotificationConfig config = MinimalConfig() with
        {
            Authentication = WebhookAuthType.BearerToken,
            SecretEnvironmentVariable = "NON_EXISTENT_VAR",
        };
        using WebhookNotificationSink sink = CreateSink(config);

        await sink.NotifyAsync(SampleNotification(), CancellationToken.None);

        Assert.Null(_handler.CapturedRequests[0].Headers.Authorization);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidUrl()
    {
        WebhookNotificationConfig config = MinimalConfig() with { Url = "http://127.0.0.1/hook" };
        var strictValidator = new WebhookUrlValidator(new WebhookUrlSecurityPolicy());
        Assert.Throws<InvalidOperationException>(() =>
            new WebhookNotificationSink(config, strictValidator, new HttpClient(_handler)));
    }

    private WebhookNotificationConfig MinimalConfig() => new()
    {
        Url = "http://localhost:59999/webhook",
        TimeoutSeconds = 5,
        MaxRetries = 0,
        RetryDelaySeconds = 0,
    };

    private WebhookNotificationSink CreateSink(WebhookNotificationConfig config)
    {
        return new WebhookNotificationSink(config, _validator, new HttpClient(_handler)
        {
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        });
    }

    private static NotificationRecord SampleNotification() => new()
    {
        TimestampUtc = DateTimeOffset.UtcNow,
        EventType = "job.completed",
        JobId = "job-test-001",
        State = JobState.DONE,
        Summary = "Job finished successfully.",
    };

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> CapturedRequests { get; } = [];
        public List<string> CapturedBodies { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;
            CapturedRequests.Add(request);
            if (body is not null)
            {
                CapturedBodies.Add(body);
            }

            HttpResponseMessage response = ResponseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK);
            return response;
        }
    }
}
