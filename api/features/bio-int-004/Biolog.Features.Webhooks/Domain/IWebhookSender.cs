using System.Net;
using System.Text;

namespace Biolog.Features.Webhooks;

public enum WebhookDeliveryOutcome
{
    Delivered,
    TransientFailure,
    PermanentFailure,
}

public sealed record WebhookSendResult(WebhookDeliveryOutcome Outcome, string Detail);

/// <summary>The actual HTTP transport - separated from <see cref="WebhookDispatchService"/>
/// the same way BIO-RPT-001's own channel adapters are separated from its dispatch service
/// (AC02's own "separated" concern, applied here to keep the retry/dedup/audit logic
/// testable without a real network call).</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(Uri url, string body, string signatureHeader, CancellationToken cancellationToken);
}

/// <summary>
/// Real HTTP delivery. Classification mirrors ordinary HTTP retry semantics, not a novel
/// scheme: 2xx is <see cref="WebhookDeliveryOutcome.Delivered"/>; 5xx, 429 (rate limited -
/// the receiver is asking for a retry, by definition), and a network-level failure (timeout,
/// connection refused, DNS) are <see cref="WebhookDeliveryOutcome.TransientFailure"/>; every
/// other 4xx (the receiver has told us definitively this request is wrong - bad signature,
/// not found, forbidden) is <see cref="WebhookDeliveryOutcome.PermanentFailure"/> - retrying
/// an unambiguous rejection would never succeed and would just look like a flood to the
/// receiver.
///
/// <b>BIO-INT-004-ZB-01 (Zainab, High - SSRF).</b> A <see cref="WebhookDestinationBlockedException"/>
/// (thrown by <see cref="WebhookConnectCallback"/>, wired into this sender's own
/// <c>SocketsHttpHandler.ConnectCallback</c> - see <c>BioInt004Module.RegisterServices</c>) is
/// also classified <see cref="WebhookDeliveryOutcome.PermanentFailure"/>, never retried: it means
/// the destination resolved to a disallowed address (loopback/private/link-local/metadata), and
/// retrying the identical URL would resolve to the identical disallowed address every time.
/// <c>HttpClient</c> wraps whatever a connect callback throws in an <see cref="HttpRequestException"/>,
/// so <see cref="FindBlockedDestination"/> walks the exception's own <see cref="Exception.InnerException"/>
/// chain rather than catching the connect-callback exception type directly.
/// </summary>
public sealed class HttpWebhookSender(HttpClient httpClient) : IWebhookSender
{
    public async Task<WebhookSendResult> SendAsync(Uri url, string body, string signatureHeader, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Webhook-Signature", signatureHeader);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            if (FindBlockedDestination(ex) is { } blocked)
            {
                return new WebhookSendResult(WebhookDeliveryOutcome.PermanentFailure, $"destination blocked by policy: {blocked.Message}");
            }

            return new WebhookSendResult(WebhookDeliveryOutcome.TransientFailure, $"network error: {ex.Message}");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return new WebhookSendResult(WebhookDeliveryOutcome.Delivered, $"{(int)response.StatusCode} {response.StatusCode}");
            }

            var isTransient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
            return new WebhookSendResult(
                isTransient ? WebhookDeliveryOutcome.TransientFailure : WebhookDeliveryOutcome.PermanentFailure,
                $"{(int)response.StatusCode} {response.StatusCode}");
        }
    }

    private static WebhookDestinationBlockedException? FindBlockedDestination(Exception? ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is WebhookDestinationBlockedException blocked)
            {
                return blocked;
            }
        }

        return null;
    }
}
