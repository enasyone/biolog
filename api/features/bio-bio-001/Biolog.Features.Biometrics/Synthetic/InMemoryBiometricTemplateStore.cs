using System.Collections.Concurrent;
using Biolog.Features.Biometrics.Domain;
using Biolog.Features.Biometrics.Matching;

namespace Biolog.Features.Biometrics.Synthetic;

/// <summary>
/// BIO-BIO-001-AC05/AC03: an in-process, in-memory <see cref="IBiometricTemplateStore"/> for
/// local development and this story's own tests - explicitly NOT a real (e.g. MySQL-backed)
/// persistence layer; a later story implements this interface against one without
/// <see cref="Service.BiometricService"/> changing shape (mirrors
/// <c>InMemoryDocumentSessionStore</c>'s identical scope note). Keyed by
/// (tenantId, personId, modality) so a lookup for one tenant/person can never return another's
/// templates - the same tenant-isolation discipline every other feature's in-memory store
/// enforces.
///
/// <b>BIO-BIO-001-F02 (Zainab round 3, High).</b> The key used to be a delimiter-joined string
/// (<c>tenantId + '\0' + personId + '\0' + modality</c>). <see cref="BiometricTemplate.Create"/>
/// and <see cref="Service.BiometricService"/> accept any non-empty string, including one
/// containing the delimiter itself, so two DIFFERENT identity tuples could produce the SAME
/// joined string - e.g. <c>tenantId="a\0b", personId="c"</c> and <c>tenantId="a",
/// personId="b\0c"</c> both joined to <c>"a\0b\0c\0Fingerprint"</c> - crossing the tenant
/// boundary. The key is now a genuine <see cref="ValueTuple{T1,T2,T3}"/>: its structural equality
/// compares each component independently, so there is no delimiter for any input to collide
/// through, regardless of what characters a tenant/person id contains.
/// </summary>
public sealed class InMemoryBiometricTemplateStore : IBiometricTemplateStore
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId, BiometricModality Modality), List<BiometricTemplate>> _templates = new();

    public Task SaveAsync(BiometricTemplate template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        var list = _templates.GetOrAdd(Key(template.TenantId, template.PersonId, template.Modality), _ => new List<BiometricTemplate>());
        lock (list)
        {
            list.Insert(0, template);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BiometricTemplate>> GetTemplatesAsync(
        string tenantId, string personId, BiometricModality modality, CancellationToken cancellationToken)
    {
        if (!_templates.TryGetValue(Key(tenantId, personId, modality), out var list))
        {
            return Task.FromResult<IReadOnlyList<BiometricTemplate>>(Array.Empty<BiometricTemplate>());
        }
        lock (list)
        {
            return Task.FromResult<IReadOnlyList<BiometricTemplate>>(list.ToArray());
        }
    }

    private static (string TenantId, string PersonId, BiometricModality Modality) Key(string tenantId, string personId, BiometricModality modality) =>
        (tenantId, personId, modality);
}
