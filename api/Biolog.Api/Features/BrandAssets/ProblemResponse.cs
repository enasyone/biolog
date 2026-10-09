using System.Text.Json.Serialization;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// One field-validation failure (ADR-0004 Decision 5.3): <c>path</c>, <c>code</c>,
/// <c>message</c>.
/// </summary>
public sealed record ValidationErrorItem(string Path, string Code, string Message);

/// <summary>
/// The platform's standard problem-response envelope (ADR-0004 Decision 2.1), produced
/// locally for this feature. <c>contracts/js/api.mjs</c> is the frozen, authoritative
/// definition of this shape; no .NET port of it exists anywhere in this host yet, and
/// creating one is shared host-level infrastructure this story does not have standing to
/// add without Kemi's sign-off (the ADR itself says migrating even the three existing JS
/// error classes onto a shared base is deliberately left to later work). This class
/// reproduces the wire shape ADR-0004 §2.1 specifies - <c>code</c>/<c>message</c>/
/// <c>status</c>/<c>correlation_id</c>/<c>detail</c>?/<c>errors</c>? - scoped to this
/// module only, so a future shared C# port is additive, not a breaking rename of what
/// clients already received from this endpoint.
/// </summary>
public sealed class ProblemResponse
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("status")]
    public required int Status { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("detail")]
    public object? Detail { get; init; }

    [JsonPropertyName("errors")]
    public IReadOnlyList<ValidationErrorItem>? Errors { get; init; }
}

/// <summary>
/// A brand-asset request error that already knows its own HTTP shape (mirrors
/// <c>TenantContextError</c>/<c>ConcurrencyError</c>'s <c>(message, code, status, detail)</c>
/// shape from the JS contracts, per ADR-0004 §2.2).
/// </summary>
public class BrandAssetApiException(string message, string code, int status, object? detail = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public object? Detail { get; } = detail;
}

public sealed class ValidationFailedException(IReadOnlyList<ValidationErrorItem> errors)
    : BrandAssetApiException("request failed validation", "validation_failed", 422)
{
    public IReadOnlyList<ValidationErrorItem> Errors { get; } = errors;
}

public sealed class BrandAssetNotFoundException()
    : BrandAssetApiException("brand asset not found", "not_found", 404);

public sealed class IdempotencyKeyRequiredException()
    : BrandAssetApiException("Idempotency-Key header is required for this operation", "idempotency_key_required", 428);

public sealed class IdempotencyKeyReuseException()
    : BrandAssetApiException("Idempotency-Key was reused with different request content", "idempotency_key_reuse", 409);

public sealed class IdempotencyWaitTimeoutException()
    : BrandAssetApiException("timed out waiting for a concurrent identical request to complete", "idempotency_wait_timeout", 503);

/// <summary>
/// Tenant authentication failed (<see cref="ITenantAuthenticator"/>): no id, no secret, a
/// malformed id, or an id/secret pair the credential store does not accept - including a
/// caller presenting a DIFFERENT tenant's own valid secret alongside this tenant's id
/// (BIO-CRD-001-QA-01). Every one of those cases returns this identical exception, by
/// design: distinguishing them in the response would let a caller learn which part of a
/// forged request was wrong, which is itself an oracle over which tenant ids exist.
/// </summary>
public sealed class TenantAuthenticationFailedException()
    : BrandAssetApiException(
        $"tenant authentication failed; provide valid {HeaderTenantAuthenticator.TenantIdHeaderName} and {HeaderTenantAuthenticator.TenantSecretHeaderName} headers",
        "tenant_authentication_failed", 401);

public sealed class MalwareDetectedException(IReadOnlyList<string> findings)
    : BrandAssetApiException("upload rejected: malware scan flagged this content", "malware_detected", 422)
{
    public IReadOnlyList<string> Findings { get; } = findings;
}

public static class ProblemResponseFactory
{
    public static ProblemResponse From(BrandAssetApiException exception, string correlationId)
    {
        return new ProblemResponse
        {
            Code = exception.Code,
            Message = exception.Message,
            Status = exception.Status,
            CorrelationId = correlationId,
            Detail = exception.Detail,
            Errors = exception is ValidationFailedException validation ? validation.Errors : null,
        };
    }
}
