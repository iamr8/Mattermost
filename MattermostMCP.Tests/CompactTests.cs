using System.Globalization;
using MattermostMcp.Output;

namespace MattermostMcp.Tests;

public class CompactTests
{
    // Break: format with the current culture, so a Buddhist-calendar machine shows year 2569.
    [Theory]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void Iso_does_not_depend_on_the_current_culture(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal("2026-09-21T14:13:23Z", Compact.Iso(1_790_000_003_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
