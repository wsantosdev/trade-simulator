using TradeSimulator.Exchange.Features.ImportInstruments;
using TradeSimulator.Exchange.Infrastructure.Sources;

namespace TradeSimulator.Exchange.Tests.Instruments;

public sealed class BvbgInstrumentSourceTests : IDisposable
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <Document xmlns="urn:bvmf.052.01.xsd"><BizFileHdr><Xchg>
          <BizGrp><Document xmlns="urn:bvmf.100.02.xsd"><Instrm>
            <FinInstrmAttrCmon><Mkt>10</Mkt><Desc>PETROBRAS   PN      N2</Desc></FinInstrmAttrCmon>
            <InstrmInf><EqtyInf><TckrSymb>PETR4</TckrSymb></EqtyInf></InstrmInf>
          </Instrm></Document></BizGrp>
          <BizGrp><Document xmlns="urn:bvmf.100.02.xsd"><Instrm>
            <FinInstrmAttrCmon><Mkt>20</Mkt><Desc>PETROBRAS   PN      N2</Desc></FinInstrmAttrCmon>
            <InstrmInf><EqtyInf><TckrSymb>PETR4F</TckrSymb></EqtyInf></InstrmInf>
          </Instrm></Document></BizGrp>
          <BizGrp><Document xmlns="urn:bvmf.100.02.xsd"><Instrm>
            <FinInstrmAttrCmon><Mkt>70</Mkt><Desc>PETR OPTION</Desc></FinInstrmAttrCmon>
            <InstrmInf><OptnOnEqtsInf><TckrSymb>PETRA100</TckrSymb></OptnOnEqtsInf></InstrmInf>
          </Instrm></Document></BizGrp>
          <BizGrp><Document xmlns="urn:bvmf.100.02.xsd"><Instrm>
            <FinInstrmAttrCmon><Mkt>10</Mkt><Desc>VALE        ON      NM</Desc></FinInstrmAttrCmon>
            <InstrmInf><EqtyInf><TckrSymb>VALE3</TckrSymb></EqtyInf></InstrmInf>
          </Instrm></Document></BizGrp>
        </Xchg></BizFileHdr></Document>
        """;

    private readonly string _path = Path.GetTempFileName();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public async Task Reads_only_cash_market_equities_with_normalized_names()
    {
        await File.WriteAllTextAsync(_path, Sample);

        var instruments = await Collect(new BvbgInstrumentSource().ReadAsync(_path, CancellationToken.None));

        Assert.Equal(
            [new ListedInstrument("PETR4", "PETROBRAS PN N2"), new ListedInstrument("VALE3", "VALE ON NM")],
            instruments);
    }

    [Fact]
    public async Task Malformed_xml_is_reported_as_invalid_data()
    {
        await File.WriteAllTextAsync(_path, Sample[..^40]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Collect(new BvbgInstrumentSource().ReadAsync(_path, CancellationToken.None)));
    }

    private static async Task<List<ListedInstrument>> Collect(IAsyncEnumerable<ListedInstrument> source)
    {
        var list = new List<ListedInstrument>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
