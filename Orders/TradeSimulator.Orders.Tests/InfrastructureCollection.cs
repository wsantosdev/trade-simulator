using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Orders.Tests;

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
