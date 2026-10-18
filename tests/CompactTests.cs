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

    // Break: use the default JSON encoder, which turns every Persian letter into a six-character \uXXXX escape.
    [Fact]
    public void Non_ascii_text_is_written_as_is_and_the_json_stays_valid()
    {
        var json = Compact.Serialize(new FileView("id", "\u0633\u0644\u0627\u0645 & <b>\n\"q\"", 1, "text/plain"));

        Assert.Equal("{\"id\":\"id\",\"name\":\"\u0633\u0644\u0627\u0645 & <b>\\n\\\"q\\\"\",\"size\":1,\"mime\":\"text/plain\"}", json);
        using var parsed = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("\u0633\u0644\u0627\u0645 & <b>\n\"q\"", parsed.RootElement.GetProperty("name").GetString());
    }
}
