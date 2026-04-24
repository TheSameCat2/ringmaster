using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Ringmaster.Core.Configuration;
using Ringmaster.Core.Jobs;
using Ringmaster.Core.Serialization;

namespace Ringmaster.Infrastructure.Persistence;

public sealed class WebhookNotificationSink : INotificationSink, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly WebhookNotificationConfig _config;
    private readonly WebhookUrlValidator _urlValidator;

    public WebhookNotificationSink(WebhookNotificationConfig config, WebhookUrlValidator urlValidator)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _urlValidator = urlValidator ?? throw new ArgumentNullException(nameof(urlValidator));
        _urlValidator.Validate(config.Url);

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = false,
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, config.TimeoutSeconds)),
        };
    }

    public WebhookNotificationSink(WebhookNotificationConfig config, WebhookUrlValidator urlValidator, HttpClient httpClient)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _urlValidator = urlValidator ?? throw new ArgumentNullException(nameof(urlValidator));
        _urlValidator.Validate(config.Url);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task NotifyAsync(NotificationRecord notification, CancellationToken cancellationToken)
    {
        if (_config.AllowedEventTypes.Count > 0
            && !_config.AllowedEventTypes.Contains(notification.EventType, StringComparer.Ordinal))
        {
            return;
        }

        string payload = RingmasterJsonSerializer.SerializeCompact(notification);
        string? secret = ReadSecret();

        int attempt = 0;
        while (true)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _config.Url);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                AddAuthentication(request, payload, secret);

                using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Delivery failures are swallowed after retries so a misconfigured
                // webhook cannot block job processing.
            }

            if (attempt >= Math.Max(0, _config.MaxRetries))
            {
                break;
            }

            attempt++;
            int delayMs = Math.Max(0, _config.RetryDelaySeconds) * attempt * 1000;
            await Task.Delay(delayMs, cancellationToken);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private string? ReadSecret()
    {
        if (string.IsNullOrWhiteSpace(_config.SecretEnvironmentVariable))
        {
            return null;
        }

        return Environment.GetEnvironmentVariable(_config.SecretEnvironmentVariable);
    }

    private void AddAuthentication(HttpRequestMessage request, string payload, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        switch (_config.Authentication)
        {
            case WebhookAuthType.BearerToken:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                break;

            case WebhookAuthType.HmacSha256:
                using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                {
                    byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                    string signature = Convert.ToHexString(hash).ToLowerInvariant();
                    request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
                }

                break;
        }
    }
}
