using System.Reflection;
using Biolog.Features.DeviceAdapters;
using Xunit;

namespace Biolog.Features.DeviceAdapters.Tests;

/// <summary>
/// BIO-DEV-003-AC04: "No vendor SDK type leaks into domain contracts." The C# compiler
/// already refuses to build a public member that exposes an internal type ("Inconsistent
/// accessibility"), which is why every type under <c>VendorSdks/**</c> is declared
/// <c>internal</c> - this test makes that structural guarantee explicit and machine-checked
/// rather than merely relying on a reviewer noticing the modifier, and would fail loudly if
/// a future change widened one of those types to <c>public</c> even before anything actually
/// tried to expose it on a public signature.
/// </summary>
public sealed class VendorSdkIsolationTests
{
    private static readonly Assembly FeatureAssembly = typeof(IDeviceAdapter).Assembly;

    private static IEnumerable<Type> VendorSdkTypes() =>
        FeatureAssembly.GetTypes().Where(t => t.Namespace == "Biolog.Features.DeviceAdapters.VendorSdks");

    [Fact]
    public void AC04_the_vendor_sdks_namespace_is_not_empty_so_this_test_is_not_vacuous()
    {
        Assert.NotEmpty(VendorSdkTypes());
    }

    [Fact]
    public void AC04_every_vendor_sdk_type_is_internal_not_public()
    {
        var leaked = VendorSdkTypes().Where(t => t.IsPublic || t.IsNestedPublic).ToList();

        Assert.True(leaked.Count == 0, "Vendor SDK type(s) marked public: " + string.Join(", ", leaked.Select(t => t.FullName)));
    }

    [Fact]
    public void AC04_no_public_method_anywhere_in_this_assembly_declares_a_vendor_sdk_parameter_or_return_type()
    {
        var vendorSdkTypes = VendorSdkTypes().ToHashSet();
        var offenders = new List<string>();

        foreach (var type in FeatureAssembly.GetTypes().Where(t => t.IsPublic))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var signatureTypes = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType);
                foreach (var signatureType in signatureTypes)
                {
                    var underlying = UnwrapGenericArgument(signatureType);
                    if (vendorSdkTypes.Contains(underlying))
                    {
                        offenders.Add($"{type.FullName}.{method.Name} exposes {underlying.FullName}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Vendor SDK type(s) leaked into a public signature: " + string.Join("; ", offenders));
    }

    /// <summary>Unwraps <c>Task&lt;T&gt;</c>/<c>Task</c> so a leaked vendor type inside an
    /// async return type is still caught, not hidden behind the Task wrapper.</summary>
    private static Type UnwrapGenericArgument(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>) ? type.GetGenericArguments()[0] : type;
}
