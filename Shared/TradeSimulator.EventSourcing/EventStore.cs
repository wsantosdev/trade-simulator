using System.Reflection;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace TradeSimulator.EventSourcing;

public sealed record EventStoreOptions(JsonSerializerOptions Json, Assembly[] Assemblies)
{
    // Stored event_type names that no longer match a type name (legacy) -> current name.
    public IReadOnlyDictionary<string, string>? TypeAliases { get; init; }
}

public sealed record AppendedEvent(long Id, IEvent Event, string Type, string Payload);

public sealed record StoredEvent(long Id, int Version, string Type, IEvent Event);

public sealed class CorruptEventStreamException(string message, Exception? inner = null) : Exception(message, inner);

// Appends serialized events to the schema's `events` table and reads a stream back.
// It never exposes read models, projections or cross-stream queries.
public sealed class EventStore
{
    private const string InsertSql = """
        INSERT INTO events (stream_id, event_type, version, occurred_at, payload)
        VALUES (@stream, @type, @version, @occurredAt, @payload)
        RETURNING id;
        """;

    private const string ReadSql = """
        SELECT id, version, event_type, payload::text FROM events
        WHERE stream_id = @stream AND version > @afterVersion
        ORDER BY version;
        """;

    private readonly EventStoreOptions _options;
    private readonly EventTypeRegistry _registry;

    public EventStore(EventStoreOptions options)
    {
        _options = options;
        _registry = new EventTypeRegistry(options.Assemblies, options.TypeAliases);
    }

    public string Serialize(IEvent @event) => JsonSerializer.Serialize(@event, @event.GetType(), _options.Json);

    public IEvent? Deserialize(string type, string payload)
    {
        if (_registry.Find(type) is not { } clrType)
            return null;
        try
        {
            return JsonSerializer.Deserialize(payload, clrType, _options.Json) as IEvent;
        }
        catch (JsonException exception)
        {
            throw new CorruptEventStreamException($"Event '{type}' could not be deserialized.", exception);
        }
    }

    // firstVersion is the version of the first event: the entity version after applying it.
    public async Task<IReadOnlyList<AppendedEvent>> AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IEnumerable<IEvent> events,
        int firstVersion,
        CancellationToken ct)
    {
        var appended = new List<AppendedEvent>();
        var version = firstVersion;
        foreach (var @event in events)
        {
            var type = @event.GetType().Name;
            var payload = Serialize(@event);
            await using var command = new NpgsqlCommand(InsertSql, connection, transaction);
            command.Parameters.AddWithValue("stream", @event.StreamId);
            command.Parameters.AddWithValue("type", type);
            command.Parameters.AddWithValue("version", version++);
            command.Parameters.AddWithValue("occurredAt", @event.OccurredAt.ToUniversalTime());
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = payload });

            if (await command.ExecuteScalarAsync(ct) is not long id)
                throw new InvalidOperationException("The event insert did not return an id.");
            appended.Add(new AppendedEvent(id, @event, type, payload));
        }

        return appended;
    }

    public async Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(
        NpgsqlConnection connection, Guid streamId, CancellationToken ct, int afterVersion = 0)
    {
        await using var command = new NpgsqlCommand(ReadSql, connection);
        command.Parameters.AddWithValue("stream", streamId);
        command.Parameters.AddWithValue("afterVersion", afterVersion);

        var stream = new List<StoredEvent>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var type = reader.GetString(2);
            var @event = Deserialize(type, reader.GetString(3))
                ?? throw new CorruptEventStreamException($"Unknown event type '{type}'.");
            stream.Add(new StoredEvent(reader.GetInt64(0), reader.GetInt32(1), type, @event));
        }

        return stream;
    }
}
