namespace Biolog.Features.Authentication;

/// <summary>Thrown when a status-change request's <c>ExpectedVersion</c> does not match the
/// stored version, matching <c>NotificationRuleConcurrencyConflictException</c>'s (and before
/// it, <c>DeviceConcurrencyConflictException</c>'s) own shape.</summary>
public sealed class AuthProviderConcurrencyConflictException(int currentVersion)
    : Exception($"expected a different version than the current {currentVersion}")
{
    public int CurrentVersion { get; } = currentVersion;
}
