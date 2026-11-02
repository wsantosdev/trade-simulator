using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace TradeSimulator.Orders.Infrastructure.ReadModel;

public static class MongoSetup
{
    private static readonly Lock Gate = new();
    private static bool _done;

    // Guids are stored with the standard (UUID) representation; must run before any serializer is resolved.
    public static void Initialize()
    {
        lock (Gate)
        {
            if (_done)
                return;
            BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
            _done = true;
        }
    }
}
