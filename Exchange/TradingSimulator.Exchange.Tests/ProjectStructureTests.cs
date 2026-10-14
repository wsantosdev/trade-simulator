namespace TradingSimulator.Exchange.Tests;

public sealed class ProjectStructureTests
{
    [Fact]
    public void Exchange_assembly_is_referenced()
    {
        Assert.Equal("TradingSimulator.Exchange", typeof(Program).Assembly.GetName().Name);
    }
}
