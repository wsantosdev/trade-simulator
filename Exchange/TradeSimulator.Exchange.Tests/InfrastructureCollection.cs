using TradeSimulator.Testing;
using Xunit;

namespace TradeSimulator.Exchange.Tests;

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
