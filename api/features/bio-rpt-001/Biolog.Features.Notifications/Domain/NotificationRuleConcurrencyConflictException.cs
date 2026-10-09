namespace Biolog.Features.Notifications;

/// <summary>Thrown when a status-change request's <c>ExpectedVersion</c> does not match the
/// stored version (ADR-0001 §3.2), matching <c>DeviceConcurrencyConflictException</c>'s own
/// shape.</summary>
public sealed class NotificationRuleConcurrencyConflictException(int currentVersion)
    : Exception($"expected a different version than the current {currentVersion}")
{
    public int CurrentVersion { get; } = currentVersion;
}
