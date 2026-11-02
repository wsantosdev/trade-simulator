using System.Reflection;

namespace TradeSimulator.EventSourcing;

// Maps stored event type names to CLR types by scanning assemblies for concrete IEvent types.
public sealed class EventTypeRegistry
{
    private readonly Dictionary<string, Type> _types;

    public EventTypeRegistry(IEnumerable<Assembly> assemblies, IReadOnlyDictionary<string, string>? aliases = null)
    {
        _types = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IEvent).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .ToDictionary(type => type.Name);
        foreach (var (stored, current) in aliases ?? new Dictionary<string, string>())
            _types[stored] = _types[current];
    }

    public Type? Find(string name) => _types.GetValueOrDefault(name);
}
