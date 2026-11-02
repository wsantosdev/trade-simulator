using Npgsql;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace TradeSimulator.Testing;

// One set of containers per test project; each test class gets its own database through CreateDatabaseAsync.
public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();

    private readonly MongoDbContainer _mongo = new MongoDbBuilder("mongo:7").Build();

    public string MongoConnection => _mongo.GetConnectionString();
    public string RabbitMqUri => _rabbit.GetConnectionString();

    public async Task InitializeAsync() =>
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync(), _mongo.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbit.DisposeAsync();
        await _mongo.DisposeAsync();
    }

    public string CreateMongoDatabaseName() => "t_" + Guid.NewGuid().ToString("N");

    public async Task<string> CreateDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await create.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
