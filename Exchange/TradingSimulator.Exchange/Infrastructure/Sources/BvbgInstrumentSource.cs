using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml;
using TradingSimulator.Exchange.Common;
using TradingSimulator.Exchange.Features.ImportInstruments;

namespace TradingSimulator.Exchange.Infrastructure.Sources;

// Streams the B3 BVBG.028 instrument register (several hundred MB), without loading it in memory.
// Each <Instrm> carries Mkt and Desc in FinInstrmAttrCmon, followed by the type-specific InstrmInf;
// equities are <EqtyInf> with the ticker in <TckrSymb>. Only the cash market (Mkt = 10) is kept:
// the other markets are fractional (20), auction (17), odd-lot/forward (21, 30) and derivatives.
public sealed partial class BvbgInstrumentSource : IInstrumentSource
{
    private const string CashMarket = "10";

    public async IAsyncEnumerable<ListedInstrument> ReadAsync(
        string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            IgnoreComments = true
        });

        string? market = null, description = null;
        var inEquity = false;

        while (await ReadAsync(reader))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (reader.LocalName == "EqtyInf")
                    inEquity = false;
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            switch (reader.LocalName)
            {
                case "Instrm":
                    market = description = null;
                    inEquity = false;
                    break;
                case "Mkt":
                    market = await TextAsync(reader);
                    break;
                case "Desc":
                    description = await TextAsync(reader);
                    break;
                case "EqtyInf":
                    inEquity = !reader.IsEmptyElement;
                    break;
                case "TckrSymb" when inEquity:
                    var ticker = await TextAsync(reader);
                    if (market == CashMarket && description is not null)
                        yield return new ListedInstrument(ticker, NormalizeName(description));
                    break;
            }
        }
    }

    public static string NormalizeName(string description) => Spaces().Replace(description.Trim(), " ");

    private static async Task<bool> ReadAsync(XmlReader reader)
    {
        try
        {
            return await reader.ReadAsync();
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The instrument file is not valid XML.", exception);
        }
    }

    // Leaves the reader on the text node, so the main loop's next read moves on to the end tag.
    private static async Task<string> TextAsync(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return string.Empty;

        return await ReadAsync(reader) && reader.NodeType == XmlNodeType.Text ? reader.Value : string.Empty;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
