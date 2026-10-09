namespace Biolog.Api.Modules;

/// <summary>
/// Thrown by <see cref="ModuleHost"/> when the registered modules' <c>DependsOn</c> edges
/// contain a cycle (BIO-FND-008-AC05). <see cref="UnresolvedModules"/> lists every module
/// that could not be ordered - Kahn's algorithm guarantees these are exactly the modules
/// participating in one or more cycles, so the message is a precise, actionable starting
/// point even without walking out the exact cycle path.
/// </summary>
public sealed class CircularModuleDependencyException : Exception
{
    public IReadOnlyList<string> UnresolvedModules { get; }

    public CircularModuleDependencyException(IReadOnlyList<string> unresolvedModules)
        : base($"Circular module dependency detected among: {string.Join(", ", unresolvedModules)}")
    {
        UnresolvedModules = unresolvedModules;
    }
}

/// <summary>
/// Thrown by <see cref="ModuleHost"/> when a module's <c>DependsOn</c> names a module that
/// was never registered - a configuration mistake distinct from a genuine cycle.
/// </summary>
public sealed class UnknownModuleDependencyException : Exception
{
    public UnknownModuleDependencyException(string module, string dependency)
        : base($"Module '{module}' depends on unregistered module '{dependency}'.")
    {
    }
}
