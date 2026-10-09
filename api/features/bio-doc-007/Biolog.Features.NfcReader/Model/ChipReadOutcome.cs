using Biolog.Features.DocumentClassifier.Model;

namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// The end-to-end result of <see cref="ChipReadSession.Read"/> (BIO-DOC-007-AC02 through AC05).
///
/// <para>
/// <b>AC05, "failure to read NFC is distinct from authenticity failure", is enforced at the type
/// level, not just by field naming:</b> <see cref="ChipCommunicationSucceeded"/>/
/// <see cref="ReadFailureReason"/> describe purely whether the secure channel and data-group
/// reads happened at all - a protocol/hardware-layer question this type alone answers. Whether
/// the DATA that was successfully read is genuine is <see cref="Model.PassiveAuthenticationResult"/>'s
/// question entirely, held in its own <see cref="PassiveAuthentication"/> property, evaluated by
/// a separate call (<see cref="Model.PassiveAuthentication.Evaluate"/>) against a separate result
/// type - the two can never be conflated into one bool the way they would be if this type carried
/// a single "success" flag covering both. <see cref="PassiveAuthentication"/> is
/// <see cref="PassiveAuthenticationResult.NotAttempted"/> whenever the caller did not supply the
/// SOD/certificate material passive authentication needs (AC04: "explicit where supported" - the
/// absence of an attempt is itself an explicit, distinct state, never silently coerced into a
/// pass or a fail).
/// </para>
///
/// <para>
/// <see cref="DocumentSessionId"/> and <see cref="Classification"/> are AC03's cross-link: a
/// plain session-id string (this story does not depend on BIO-DOC-001, the same "take the id as a
/// plain parameter" pattern BIO-DOC-002's evidence adapter and BIO-DOC-005's <c>ParserCaller</c>
/// used) plus the BIO-DOC-006 classification this read was performed for, if known.
/// </para>
///
/// <para>Locked down (<c>internal init</c>, no public constructor) for the same reason as every
/// prior BIO-DOC-00x result type this epic.</para>
///
/// <para>
/// <b>BIO-DOC-007-QA-03 (Chioma's second acceptance review):</b> <c>internal init</c> on an
/// <c>IReadOnlyList&lt;T&gt;</c>-typed property only blocks reassigning the property - the
/// interface removes mutation methods at COMPILE time but not at runtime if the stored object is
/// still the caller's own mutable <c>List&lt;T&gt;</c>: a caller could downcast
/// <c>(List&lt;ChipDataGroupRead&gt;)outcome.DataGroupsRead</c> back to a mutable list and clear
/// or alter chip evidence after passive authentication had already run against it. The
/// constructor now wraps a FRESH copy of each collection in <c>List&lt;T&gt;.AsReadOnly()</c>
/// (a <c>ReadOnlyCollection&lt;T&gt;</c>, a genuinely distinct type from <c>List&lt;T&gt;</c> that
/// cannot be downcast back to one) rather than storing the caller's list directly - the same
/// "byte[] getters returning a mutable shared array" lesson BIO-DOC-007-QA-02 taught for key
/// material, applied here to collections.
/// </para>
/// </summary>
public sealed record ChipReadOutcome
{
    public string DocumentSessionId { get; internal init; } = null!;
    public DocumentClassCandidate? Classification { get; internal init; }

    public bool ChipCommunicationSucceeded { get; internal init; }
    public string? ReadFailureReason { get; internal init; }

    public IReadOnlyList<ChipDataGroupRead> DataGroupsRead { get; internal init; } = [];
    public IReadOnlyList<ChipDataGroupId> DataGroupsUnavailable { get; internal init; } = [];

    public PassiveAuthenticationResult PassiveAuthentication { get; internal init; } = PassiveAuthenticationResult.NotAttempted;

    internal ChipReadOutcome(
        string documentSessionId,
        DocumentClassCandidate? classification,
        bool chipCommunicationSucceeded,
        string? readFailureReason,
        IReadOnlyList<ChipDataGroupRead> dataGroupsRead,
        IReadOnlyList<ChipDataGroupId> dataGroupsUnavailable,
        PassiveAuthenticationResult passiveAuthentication)
    {
        DocumentSessionId = documentSessionId;
        Classification = classification;
        ChipCommunicationSucceeded = chipCommunicationSucceeded;
        ReadFailureReason = readFailureReason;
        // A fresh List<T> copy wrapped in AsReadOnly() - not merely the caller's original list
        // viewed through the IReadOnlyList interface - so a caller can never downcast the
        // exposed collection back to a mutable List<T> and alter chip evidence after the fact
        // (BIO-DOC-007-QA-03, Chioma's second acceptance review).
        DataGroupsRead = new List<ChipDataGroupRead>(dataGroupsRead).AsReadOnly();
        DataGroupsUnavailable = new List<ChipDataGroupId>(dataGroupsUnavailable).AsReadOnly();
        PassiveAuthentication = passiveAuthentication;
    }
}
