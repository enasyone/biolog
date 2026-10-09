namespace Biolog.Api.Observability;

/// <summary>
/// An explicit, call-site-level assertion that a value is safe to log verbatim
/// (BIO-FND-009-QA-02). Wrapping a value here is the ONLY way a log statement outside a
/// trusted framework category gets its value rendered instead of withheld by default - the
/// positive, enforceable contract Seyi's re-review required, mirroring ADR-0003 Decision
/// 3's "an allowlist, never a denylist" applied to log values instead of envelope fields.
///
/// A <see cref="string"/> has no inherent classification: the exact same CLR type carries a
/// user id and a biometric template. BIO-FND-009-QA-01 closed the case where a caller-defined
/// TYPE leaked through an unrecognised name; treating every string (and array of strings) as
/// "safe by type" reopened the identical hole one level down (QA-02) - a classified value
/// almost always <em>is</em> a string. There is no type check that can tell them apart, so
/// the decision is moved to the one place that actually knows: the call site.
///
/// Wrapping a name-classified value (see <see cref="SensitiveLogKeys"/>) still redacts it -
/// this is an opt-in for values with no name signal, not an override of one that exists.
/// </summary>
public readonly record struct SafeLogValue(object? Value)
{
    public static SafeLogValue Of(object? value) => new(value);
}
