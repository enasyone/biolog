namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>Thrown when a caller's <c>expectedVersion</c> no longer matches an
/// <see cref="AuthenticityAssessment"/>'s current version - the same optimistic-concurrency (CAS)
/// discipline ADR-0001 Decision 3 establishes for every mutable BIOLOG aggregate
/// (<c>DocumentSession.Version</c>, <c>ParserRecord.ConcurrencyVersion</c>, etc.).</summary>
public sealed class AuthenticityAssessmentConcurrencyException(string message) : InvalidOperationException(message);
