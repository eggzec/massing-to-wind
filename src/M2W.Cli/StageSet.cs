using System.Reflection;
using M2W.Geometry.Stages;

namespace M2W.Cli;

/// <summary>
/// The stage implementations m2w runs. <see cref="Discover"/> looks in each role's assembly for the single public
/// class implementing the stage interface. A null stage means "not implemented yet"; for repair, m2w then uses
/// <see cref="StubRepairer"/>. Tests construct a StageSet directly with fakes.
/// </summary>
public sealed class StageSet
{
    public const string RepairAssembly = "M2W.Repair";
    public const string CheckerAssembly = "M2W.Checker";
    public const string GeneratorAssembly = "M2W.Generator";

    public ICaseRepairer? Repairer { get; init; }
    public ICaseChecker? Checker { get; init; }
    public ICaseGenerator? Generator { get; init; }

    public static StageSet Discover() => new()
    {
        Repairer = Find<ICaseRepairer>(RepairAssembly),
        Checker = Find<ICaseChecker>(CheckerAssembly),
        Generator = Find<ICaseGenerator>(GeneratorAssembly),
    };

    /// <summary>Returns a new instance of the one implementation of <typeparamref name="T"/>, or null if there is none.</summary>
    public static T? Find<T>(string assemblyName) where T : class
    {
        Assembly asm;
        try
        {
            asm = Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var types = asm.GetExportedTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(T).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();

        return types.Count switch
        {
            0 => null,
            1 => (T)Activator.CreateInstance(types[0])!,
            _ => throw new InvalidOperationException(
                $"{assemblyName} has {types.Count} public implementations of {typeof(T).Name} " +
                $"({string.Join(", ", types.Select(t => t.FullName))}); m2w needs exactly one."),
        };
    }
}
