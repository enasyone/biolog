namespace Biolog.Features.Notifications;

/// <summary>
/// BIO-RPT-001-AC04: "Sensitive information is minimized per channel." Applies ADR-0003
/// Decision 3's own rule - redaction is an allowlist, never a denylist - at this project's
/// own layer: a channel names exactly which payload keys it is allowed to carry, and
/// <see cref="Project"/> copies only those.
///
/// <see cref="EventClassification.Personal"/> and <see cref="EventClassification.Sensitive"/>
/// short-circuit to an EMPTY projection regardless of any channel's own allowlist: this
/// foundation story does not define a per-field allowlist for personal/sensitive data (ADR-
/// 0003 Decision 3: those levels "may appear... only through an allowlist" - the absence of
/// one here means the safe, fail-closed default is to carry none of it), so every channel
/// falls back to whatever generic, payload-free summary its own <c>Render</c> produces for
/// that case. <see cref="EventClassification.Public"/>/<see cref="EventClassification.Internal"/>
/// are the only levels this story actually allowlists per-channel fields for.
/// </summary>
public static class PayloadAllowlist
{
    public static IReadOnlyDictionary<string, string> Project(DomainEventEnvelope envelope, IReadOnlySet<string> allowedKeys)
    {
        if (envelope.PayloadClassification is EventClassification.Personal or EventClassification.Sensitive)
        {
            return new Dictionary<string, string>();
        }

        var projected = new Dictionary<string, string>();
        foreach (var key in allowedKeys)
        {
            if (envelope.Payload.TryGetValue(key, out var value))
            {
                projected[key] = value;
            }
        }

        return projected;
    }
}
