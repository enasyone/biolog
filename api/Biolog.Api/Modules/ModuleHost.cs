namespace Biolog.Api.Modules;

/// <summary>
/// Orders <see cref="IBiologModule"/> instances by their declared dependencies and drives
/// their three lifecycle hooks in that order (BIO-FND-008-AC05). Ordering happens once, in
/// the constructor, using Kahn's algorithm: a module registers/configures only after every
/// module it depends on already has, and a cycle is rejected immediately with a clear
/// exception instead of stack-overflowing or silently registering in an arbitrary order.
/// </summary>
public sealed class ModuleHost
{
    private readonly IReadOnlyList<IBiologModule> _orderedModules;

    public ModuleHost(IEnumerable<IBiologModule> modules)
    {
        _orderedModules = TopologicalSort(modules.ToList());
    }

    /// <summary>Modules in dependency order. Exposed for tests; not needed by <c>Program.cs</c>.</summary>
    public IReadOnlyList<IBiologModule> OrderedModules => _orderedModules;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in _orderedModules)
        {
            module.RegisterServices(services, configuration);
        }
    }

    public void Configure(IApplicationBuilder app)
    {
        foreach (var module in _orderedModules)
        {
            module.Configure(app);
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        foreach (var module in _orderedModules)
        {
            module.MapEndpoints(endpoints);
        }
    }

    /// <summary>
    /// Kahn's algorithm. Internal (not private) so unit tests can exercise ordering and
    /// cycle detection directly, without constructing a full <see cref="ModuleHost"/> and
    /// without needing an ASP.NET Core host.
    /// </summary>
    internal static IReadOnlyList<IBiologModule> TopologicalSort(IReadOnlyList<IBiologModule> modules)
    {
        var byName = new Dictionary<string, IBiologModule>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (!byName.TryAdd(module.Name, module))
            {
                throw new InvalidOperationException($"Duplicate module name '{module.Name}'.");
            }
        }

        foreach (var module in modules)
        {
            foreach (var dependency in module.DependsOn)
            {
                if (!byName.ContainsKey(dependency))
                {
                    throw new UnknownModuleDependencyException(module.Name, dependency);
                }
            }
        }

        var inDegree = modules.ToDictionary(m => m.Name, _ => 0, StringComparer.Ordinal);
        var dependents = modules.ToDictionary(m => m.Name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var module in modules)
        {
            foreach (var dependency in module.DependsOn)
            {
                inDegree[module.Name]++;
                dependents[dependency].Add(module.Name);
            }
        }

        // Deterministic ordering among modules that are simultaneously ready, so the
        // registration order does not depend on the input list's incidental order.
        var ready = new Queue<string>(
            modules.Where(m => inDegree[m.Name] == 0).Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal));
        var ordered = new List<IBiologModule>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (ready.Count > 0)
        {
            var name = ready.Dequeue();
            visited.Add(name);
            ordered.Add(byName[name]);
            foreach (var dependent in dependents[name].OrderBy(n => n, StringComparer.Ordinal))
            {
                if (--inDegree[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (ordered.Count != modules.Count)
        {
            var unresolved = modules.Select(m => m.Name).Where(n => !visited.Contains(n)).ToList();
            throw new CircularModuleDependencyException(unresolved);
        }

        return ordered;
    }
}
