using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bluetech.Display.Recorder;

/// <summary>Oggetto JSON ordinato, serializzato a mano per non dipendere da librerie esterne su .NET Framework.</summary>
public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
{
    readonly List<KeyValuePair<string, object>> _items = new();

    public void Add(string key, object value) => _items.Add(new KeyValuePair<string, object>(key, value));

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var sb = new StringBuilder();
        Write(sb, this);
        return sb.ToString();
    }

    static void Write(StringBuilder sb, object value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                WriteString(sb, s);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int or long or short or byte:
                sb.Append(System.Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
            case double d:
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                break;
            case JsonObject obj:
                sb.Append('{');
                var first = true;
                foreach (var pair in obj._items)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, pair.Key);
                    sb.Append(':');
                    Write(sb, pair.Value);
                }
                sb.Append('}');
                break;
            case IEnumerable list:
                sb.Append('[');
                var firstItem = true;
                foreach (var item in list)
                {
                    if (!firstItem) sb.Append(',');
                    firstItem = false;
                    Write(sb, item);
                }
                sb.Append(']');
                break;
            default:
                WriteString(sb, value.ToString());
                break;
        }
    }

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
