using TradingSimulator.Testing;
using Xunit;

namespace TradingSimulator.Orders.Tests;

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
