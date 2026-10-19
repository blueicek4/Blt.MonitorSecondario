using System.Collections.Generic;
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
}
