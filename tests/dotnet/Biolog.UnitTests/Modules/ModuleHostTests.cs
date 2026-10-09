using Biolog.Api.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Biolog.UnitTests.Modules;

/// <summary>Developer tests for BIO-FND-008-AC05 (module registration pattern prevents
/// circular dependencies).</summary>
public sealed class ModuleHostTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public void AC05_orders_a_dependency_before_its_dependent_even_when_given_out_of_order()
    {
        var log = new List<string>();
        var b = new FakeModule("B", new[] { "A" }, log);
        var a = new FakeModule("A", log: log);

        var host = new ModuleHost(new IBiologModule[] { b, a });
        host.RegisterServices(new ServiceCollection(), EmptyConfiguration);

        Assert.Equal(new[] { "A", "B" }, log);
    }

    [Fact]
    public void AC05_orders_a_three_link_chain_correctly_regardless_of_input_order()
    {
        var log = new List<string>();
        var c = new FakeModule("C", new[] { "B" }, log);
        var a = new FakeModule("A", log: log);
        var b = new FakeModule("B", new[] { "A" }, log);

        var host = new ModuleHost(new IBiologModule[] { c, b, a });
        host.RegisterServices(new ServiceCollection(), EmptyConfiguration);

        Assert.Equal(new[] { "A", "B", "C" }, log);
    }

    [Fact]
    public void AC05_two_module_cycle_is_rejected_with_both_names_reported()
    {
        var a = new FakeModule("A", new[] { "B" });
        var b = new FakeModule("B", new[] { "A" });

        var ex = Assert.Throws<CircularModuleDependencyException>(
            () => new ModuleHost(new IBiologModule[] { a, b }));

        Assert.Equal(new[] { "A", "B" }, ex.UnresolvedModules.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void AC05_self_referential_module_is_rejected_as_a_cycle()
    {
        var a = new FakeModule("A", new[] { "A" });

        Assert.Throws<CircularModuleDependencyException>(() => new ModuleHost(new IBiologModule[] { a }));
    }

    [Fact]
    public void AC05_a_cycle_hidden_among_otherwise_resolvable_modules_is_still_rejected()
    {
        // D and E form a cycle; A and B are fine on their own and must not mask it.
        var a = new FakeModule("A");
        var b = new FakeModule("B", new[] { "A" });
        var d = new FakeModule("D", new[] { "E" });
        var e = new FakeModule("E", new[] { "D" });

        var ex = Assert.Throws<CircularModuleDependencyException>(
            () => new ModuleHost(new IBiologModule[] { a, b, d, e }));

        Assert.Equal(new[] { "D", "E" }, ex.UnresolvedModules.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Dependency_on_an_unregistered_module_is_rejected()
    {
        var a = new FakeModule("A", new[] { "Ghost" });

        Assert.Throws<UnknownModuleDependencyException>(() => new ModuleHost(new IBiologModule[] { a }));
    }

    [Fact]
    public void Duplicate_module_names_are_rejected()
    {
        var a1 = new FakeModule("A");
        var a2 = new FakeModule("A");

        Assert.Throws<InvalidOperationException>(() => new ModuleHost(new IBiologModule[] { a1, a2 }));
    }

    [Fact]
    public void AC05_the_real_shell_modules_have_no_circular_dependency()
    {
        // The concrete modules Program.cs actually registers - proves AC05 holds for the
        // real shell, not only for synthetic fixtures.
        var host = new ModuleHost(new IBiologModule[] { new HealthModule(), new OpenApiModule(), new TenantModule() });

        Assert.Equal(3, host.OrderedModules.Count);
    }
}
