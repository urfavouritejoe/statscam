using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StatsCam;

/// <summary>
/// A tiny JSON writer. Hand-rolled deliberately: the mod runs inside an il2cpp process
/// where pulling in a serializer is more risk than the few lines it saves.
/// </summary>
public sealed class Json
{
    private readonly StringBuilder _sb = new();
    private readonly Stack<bool> _first = new();

    public Json() { }

    public Json Obj()
    {
        Sep();
        _sb.Append('{');
        _first.Push(true);
        return this;
    }

    public Json EndObj()
    {
        _sb.Append('}');
        _first.Pop();
        return this;
    }

    public Json Arr()
    {
        Sep();
        _sb.Append('[');
        _first.Push(true);
        return this;
    }

    public Json EndArr()
    {
        _sb.Append(']');
        _first.Pop();
        return this;
    }

    public Json Key(string k)
    {
        Sep();
        Str(k);
        _sb.Append(':');
        if (_first.Count > 0) { _first.Pop(); _first.Push(true); }
        return this;
    }

    public Json Val(string v) { Sep(); Str(v); return this; }
    public Json Val(int v) { Sep(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
    public Json Val(bool v) { Sep(); _sb.Append(v ? "true" : "false"); return this; }

    public Json Val(float v)
    {
        Sep();
        if (float.IsNaN(v) || float.IsInfinity(v)) _sb.Append('0');
        else _sb.Append(v.ToString("0.####", CultureInfo.InvariantCulture));
        return this;
    }

    public Json P(string k, string v) => Key(k).Val(v);
    public Json P(string k, int v) => Key(k).Val(v);
    public Json P(string k, float v) => Key(k).Val(v);
    public Json P(string k, bool v) => Key(k).Val(v);

    private void Sep()
    {
        if (_first.Count == 0) return;
        if (_first.Peek()) { _first.Pop(); _first.Push(false); }
        else _sb.Append(',');
    }

    private void Str(string s)
    {
        _sb.Append('"');
        if (s != null)
        {
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
        }
        _sb.Append('"');
    }

    public override string ToString() => _sb.ToString();
}
