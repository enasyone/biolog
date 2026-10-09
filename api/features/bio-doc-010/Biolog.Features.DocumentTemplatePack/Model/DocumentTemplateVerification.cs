using System.Globalization;
using System.Text.RegularExpressions;
using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.DocumentTemplatePack.Audit;

namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// AC04's "pack version is recorded on every verification": validates a caller-observed field
/// value set against one governed, PUBLISHED <see cref="DocumentTemplatePackEntry"/>'s own
/// <see cref="FieldValidationRule"/>s, and durably audits the outcome - stamped with the exact
/// pack/version/entry it ran against - before ever returning it (the same audit-before-commit
/// pattern <see cref="DocumentTemplatePack.Publish"/> and every prior BIO-DOC-00x result type in
/// this epic use).
///
/// <para>
/// <paramref name="correlatedAuthenticityDisposition"/> is an optional, purely informational
/// cross-reference to a BIO-DOC-009 <see cref="AuthenticityDisposition"/> a caller already
/// computed - recorded on the audit event and the returned outcome for traceability. This project
/// cannot construct a real BIO-DOC-009 <c>AuthenticityAssessmentResult</c> itself (see this
/// project's own <c>.csproj</c> comment), so this is the narrowest genuine use of the BIO-DOC-009
/// dependency available at this boundary: a caller who already holds one (e.g. the HTTP host,
/// after calling both features) may pass its disposition through; nothing here requires it.
/// </para>
/// </summary>
public static partial class DocumentTemplateVerification
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex Rfc3339Pattern();

    public static DocumentTemplateVerificationOutcome Verify(
        Model.DocumentTemplatePack pack,
        string entryId,
        IReadOnlyDictionary<string, string> observedFields,
        string verifiedByActorId,
        string correlationId,
        string recordedAt,
        IDocumentTemplatePackAuditSink auditSink,
        AuthenticityDisposition? correlatedAuthenticityDisposition = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(observedFields);
        ArgumentNullException.ThrowIfNull(auditSink);

        if (string.IsNullOrWhiteSpace(entryId))
        {
            throw new ArgumentException("entryId must not be blank", nameof(entryId));
        }

        if (string.IsNullOrWhiteSpace(verifiedByActorId))
        {
            throw new ArgumentException("verifiedByActorId must not be blank", nameof(verifiedByActorId));
        }

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new ArgumentException("correlationId must not be blank", nameof(correlationId));
        }

        if (!Rfc3339Pattern().IsMatch(recordedAt)
            || !DateTimeOffset.TryParse(recordedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException(
                $"recordedAt must be a full RFC 3339 date-time with an explicit 'Z' or numeric offset, got '{recordedAt}'",
                nameof(recordedAt));
        }

        if (pack.Status != DocumentTemplatePackStatus.Published)
        {
            throw new PackNotPublishedException(
                $"pack '{pack.PackId}' version '{pack.PackVersion}' is not published - a verification can only be recorded against a published pack");
        }

        var entry = pack.FindEntry(entryId)
            ?? throw new EntryNotInPackException($"pack '{pack.PackId}' version '{pack.PackVersion}' has no entry with id '{entryId}'");

        var violations = new List<DocumentTemplatePackFieldViolation>();
        foreach (var rule in entry.ValidationRules)
        {
            var hasValue = observedFields.TryGetValue(rule.FieldName, out var value) && !string.IsNullOrWhiteSpace(value);
            if (rule.Required && !hasValue)
            {
                violations.Add(new DocumentTemplatePackFieldViolation(rule.FieldName, "required field is missing"));
                continue;
            }

            if (hasValue && rule.Pattern is not null && !Regex.IsMatch(value!, rule.Pattern))
            {
                violations.Add(new DocumentTemplatePackFieldViolation(rule.FieldName, $"value does not match the expected pattern '{rule.Pattern}'"));
            }
        }

        var requiredFieldNames = entry.ValidationRules.Where(r => r.Required).Select(r => r.FieldName).ToHashSet(StringComparer.Ordinal);
        var state = violations.Count == 0
            ? DocumentTemplateVerificationState.Consistent
            : violations.Any(v => requiredFieldNames.Contains(v.FieldName))
                ? DocumentTemplateVerificationState.Failed
                : DocumentTemplateVerificationState.Inconclusive;

        var actor = new AuditActor(ActorType.Unknown, verifiedByActorId);
        var auditEvent = DocumentTemplatePackAuditEventBuilder.BuildVerificationRecorded(
            actor, pack.PackId, pack.PackVersion, entry.EntryId, state, violations.Count, correlationId, correlatedAuthenticityDisposition);

        WriteAuditEventOrThrow(auditSink, auditEvent);

        return new DocumentTemplateVerificationOutcome(
            auditEvent.EventId,
            pack.PackId,
            pack.PackVersion,
            entry.EntryId,
            entry.Country,
            entry.Issuer,
            entry.DocumentClass,
            entry.Series,
            state,
            violations,
            correlationId,
            recordedAt,
            correlatedAuthenticityDisposition);
    }

    /// <summary>The same ambiguous-write resolution every hard-audited BIOLOG feature's own
    /// <c>AuditAsync</c> helper uses.</summary>
    private static void WriteAuditEventOrThrow(IDocumentTemplatePackAuditSink auditSink, DocumentTemplatePackAuditEvent auditEvent)
    {
        try
        {
            auditSink.WriteAsync(auditEvent).GetAwaiter().GetResult();
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = auditSink.ExistsAsync(auditEvent.EventId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new DocumentTemplatePackAuditUnavailableException("audit sink unavailable: could not even verify a prior write", ex);
        }

        if (!exists)
        {
            throw new DocumentTemplatePackAuditUnavailableException("audit sink unavailable: write did not durably persist");
        }
    }
}
