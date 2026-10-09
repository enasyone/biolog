using System.Reflection;
using Biolog.Features.BiometricRetention.Audit;
using Biolog.Features.BiometricRetention.Domain;
using Biolog.Features.BiometricRetention.Service;
using Xunit;

namespace Biolog.Features.BiometricRetention.Tests;

/// <summary>BIO-IDN-001-QA-01-shaped governance checks, mirroring every prior BIO-BIO story's own.</summary>
public sealed class BiometricRetentionGovernanceTests
{
    [Fact]
    public void IRetentionPolicy_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IRetentionPolicy).IsPublic);
        Assert.False(typeof(InMemoryRetentionPolicy).IsPublic);
    }

    [Fact]
    public void IRawImageRetentionPolicy_and_InMemory_implementation_are_not_public()
    {
        Assert.False(typeof(IRawImageRetentionPolicy).IsPublic);
        Assert.False(typeof(InMemoryRawImageRetentionPolicy).IsPublic);
    }

    [Fact]
    public void ITemplateDeletionGateway_is_not_public()
    {
        Assert.False(typeof(ITemplateDeletionGateway).IsPublic);
    }

    [Fact]
    public void RetentionPolicyService_and_BiometricDeletionService_are_not_public()
    {
        Assert.False(typeof(RetentionPolicyService).IsPublic);
        Assert.False(typeof(BiometricDeletionService).IsPublic);
    }

    /// <summary>
    /// BIO-BIO-009-AC04: "Deletion propagates to permitted caches/providers without deleting
    /// required immutable audit metadata." Structural proof, not merely behavioural: the audit
    /// sink contract this feature's ENTIRE deletion path writes through has no delete or remove
    /// member of any kind, on either the interface or its concrete in-memory implementation - so
    /// nothing reachable from this feature's own deletion/retention code could delete an audit
    /// event even by a future, careless edit.
    /// </summary>
    [Fact]
    public void IBiometricRetentionAuditSink_has_no_delete_or_remove_shaped_member()
    {
        var interfaceMemberNames = typeof(IBiometricRetentionAuditSink).GetMembers().Select(m => m.Name.ToLowerInvariant());
        var concreteMemberNames = typeof(InMemoryBiometricRetentionAuditSink)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name.ToLowerInvariant());

        foreach (var names in new[] { interfaceMemberNames, concreteMemberNames })
        {
            Assert.DoesNotContain(names, name => name.Contains("delete"));
            Assert.DoesNotContain(names, name => name.Contains("remove"));
            Assert.DoesNotContain(names, name => name.Contains("clear"));
        }
    }

    /// <summary>BIO-BIO-009-AC02: the audit event type itself never carries a template or image
    /// byte payload (ADR-0003 Decision 3: a <c>secret</c>-classified value is never copied into
    /// an audit event in any form) - every property is a plain string/identifier.</summary>
    [Fact]
    public void BiometricRetentionAuditEvent_carries_no_byte_array_or_binary_shaped_field()
    {
        var properties = typeof(BiometricRetentionAuditEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.All(properties, p => Assert.True(
            p.PropertyType == typeof(string),
            $"{p.Name} must be a plain string, not {p.PropertyType.Name} - this type must never carry a biometric payload."));
    }

    [Fact]
    public void this_assembly_has_no_reference_to_any_sibling_BIO_BIO_storys_own_assembly()
    {
        var referenced = typeof(BiometricRetentionAuditEvent).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.DoesNotContain("Biolog.Features.Biometrics", referenced);
        Assert.DoesNotContain("Biolog.Features.FingerprintEnrolment", referenced);
        Assert.DoesNotContain("Biolog.Features.FaceEnrolment", referenced);
        Assert.DoesNotContain("Biolog.Features.FingerprintSearch", referenced);
        Assert.DoesNotContain("Biolog.Features.FaceSearch", referenced);
    }
}
