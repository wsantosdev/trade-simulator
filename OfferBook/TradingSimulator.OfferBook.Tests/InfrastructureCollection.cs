using TradingSimulator.Testing;
using Xunit;

namespace TradingSimulator.OfferBook.Tests;

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
