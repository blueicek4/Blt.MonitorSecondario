using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Xunit;

namespace Bluetech.Display.Recorder.Tests;

public class JsonTests
{
    [Fact]
    public void Escapes_quotes_backslashes_and_control_characters()
    {
        var json = new JsonObject
        {
            { "testo", "DES='PIZZA \"4 STAGIONI\"' C:\\cassa\r\n\t\u0001" },
            { "nullo", null },
            { "numero", 12L },
            { "vero", true },
            { "lista", new List<object> { "€", 1, new JsonObject { { "riga", 2 } } } },
        }.ToString();

        Assert.Equal(
            "{\"testo\":\"DES='PIZZA \\\"4 STAGIONI\\\"' C:\\\\cassa\\r\\n\\t\\u0001\",\"nullo\":null,\"numero\":12,\"vero\":true,\"lista\":[\"€\",1,{\"riga\":2}]}",
            json);

#if NET
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("DES='PIZZA \"4 STAGIONI\"' C:\\cassa\r\n\t\u0001", doc.RootElement.GetProperty("testo").GetString());
#endif
    }

    [Fact]
    public void Timestamps_are_iso_8601_whatever_the_regional_settings()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        var dotted = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        dotted.DateTimeFormat.TimeSeparator = ".";
        dotted.DateTimeFormat.DateSeparator = "/";
        try
        {
            Thread.CurrentThread.CurrentCulture = dotted;
            var time = new DateTimeOffset(2026, 10, 19, 20, 15, 3, 123, TimeSpan.FromHours(2));
            Assert.Equal("2026-10-19T20:15:03.123+02:00", Timestamps.Iso(time));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
