using TradingSimulator.Testing;
using Xunit;

namespace TradingSimulator.Quotes.Tests;

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "Infrastructure";
}
