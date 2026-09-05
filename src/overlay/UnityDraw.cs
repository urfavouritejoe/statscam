using System;
using System.Reflection;

namespace StatsCam.Overlay;

/// <summary>
/// The real backend: immediate-mode drawing bound to UnityEngine by reflection.
///
/// Every UnityEngine member is resolved once and cached, so the per-frame cost is a
/// cached MethodInfo.Invoke rather than a lookup. Reflection is used because the mod must
/// build and run without the generated Il2Cpp interop assemblies; if those are ever
/// referenced directly, only this file changes.
/// </summary>
internal sealed class UnityDraw : IDraw
{
    public bool Ready { get; private set; }
    public bool Failed { get; private set; }

    private ConstructorInfo _rectCtor, _colorCtor, _texCtor, _styleCtor;
    private MethodInfo _drawTexture, _label, _setGuiColor, _setPixel, _apply;
    private PropertyInfo _fontSize, _alignment, _normal, _textColor, _wordWrap, _fontStyle;
    private PropertyInfo _screenW, _screenH;
    private Type _anchorType, _fontStyleType;

    private object _white;
    private object _styleLeft, _styleCenter, _styleRight;
    private object _whiteColor;

    public int ScreenW => _screenW?.GetValue(null) as int? ?? 1920;
    public int ScreenH => _screenH?.GetValue(null) as int? ?? 1080;

    // ------------------------------------------------------------ binding

    public void Init()
    {
        if (Ready || Failed) return;

        try
        {
            var rect = Reflect.Find("UnityEngine.Rect");
            var color = Reflect.Find("UnityEngine.Color");
            var gui = Reflect.Find("UnityEngine.GUI");
            var screen = Reflect.Find("UnityEngine.Screen");
            var texType = Reflect.Find("UnityEngine.Texture2D");
            var styleType = Reflect.Find("UnityEngine.GUIStyle");
            _anchorType = Reflect.Find("UnityEngine.TextAnchor");
            _fontStyleType = Reflect.Find("UnityEngine.FontStyle");

            if (rect == null || color == null || gui == null || texType == null || styleType == null)
            {
                Journal.Warn("overlay: UnityEngine GUI types not found — overlay disabled");
                Failed = true;
                return;
            }

            _rectCtor = rect.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            _colorCtor = color.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });
            _texCtor = texType.GetConstructor(new[] { typeof(int), typeof(int) });
            _styleCtor = styleType.GetConstructor(Type.EmptyTypes);

            var texture = Reflect.Find("UnityEngine.Texture");
            _drawTexture = gui.GetMethod("DrawTexture", new[] { rect, texture });
            _label = gui.GetMethod("Label", new[] { rect, typeof(string), styleType });
            _setGuiColor = gui.GetProperty("color")?.GetSetMethod();

            _setPixel = texType.GetMethod("SetPixel", new[] { typeof(int), typeof(int), color });
            _apply = texType.GetMethod("Apply", Type.EmptyTypes);

            _fontSize = styleType.GetProperty("fontSize");
            _alignment = styleType.GetProperty("alignment");
            _normal = styleType.GetProperty("normal");
            _wordWrap = styleType.GetProperty("wordWrap");
            _fontStyle = styleType.GetProperty("fontStyle");

            var styleState = Reflect.Find("UnityEngine.GUIStyleState");
            _textColor = styleState?.GetProperty("textColor");

            _screenW = screen?.GetProperty("width");
            _screenH = screen?.GetProperty("height");

            if (_rectCtor == null || _colorCtor == null || _drawTexture == null || _label == null)
            {
                Journal.Warn("overlay: could not bind GUI members — overlay disabled");
                Failed = true;
                return;
            }

            _whiteColor = MakeColor(new Col(1, 1, 1));
            _white = _texCtor.Invoke(new object[] { 1, 1 });
            _setPixel.Invoke(_white, new[] { (object)0, 0, _whiteColor });
            _apply.Invoke(_white, null);

            _styleLeft = MakeStyle(Align.Left);
            _styleCenter = MakeStyle(Align.Center);
            _styleRight = MakeStyle(Align.Right);

            Ready = true;
            Journal.Line("overlay: GUI bound");
        }
        catch (Exception e)
        {
            Failed = true;
            Journal.Warn($"overlay: GUI binding threw — {e.Message}");
        }
    }

    private object MakeStyle(Align a)
    {
        var s = _styleCtor.Invoke(null);
        try
        {
            _wordWrap?.SetValue(s, false);
            if (_anchorType != null && _alignment != null)
            {
                var name = a switch
                {
                    Align.Center => "MiddleCenter",
                    Align.Right => "MiddleRight",
                    _ => "MiddleLeft"
                };
                _alignment.SetValue(s, Enum.Parse(_anchorType, name));
            }
        }
        catch { }
        return s;
    }

    private object MakeRect(float x, float y, float w, float h)
        => _rectCtor.Invoke(new object[] { x, y, w, h });

    private object MakeColor(Col c)
        => _colorCtor.Invoke(new object[] { c.R, c.G, c.B, c.A });

    // ------------------------------------------------------------ drawing

    public void Fill(float x, float y, float w, float h, Col c)
    {
        if (!Ready || w <= 0 || h <= 0) return;
        try
        {
            _setGuiColor?.Invoke(null, new[] { MakeColor(c) });
            _drawTexture.Invoke(null, new[] { MakeRect(x, y, w, h), _white });
            _setGuiColor?.Invoke(null, new[] { _whiteColor });
        }
        catch { }
    }

    public void Text(float x, float y, float w, float h, string s, int size, Col c,
                     Align align, bool bold)
    {
        if (!Ready || string.IsNullOrEmpty(s)) return;
        try
        {
            var style = align switch
            {
                Align.Center => _styleCenter,
                Align.Right => _styleRight,
                _ => _styleLeft
            };

            _fontSize?.SetValue(style, size);

            if (_fontStyle != null && _fontStyleType != null)
                _fontStyle.SetValue(style, Enum.Parse(_fontStyleType, bold ? "Bold" : "Normal"));

            if (_normal != null && _textColor != null)
            {
                var state = _normal.GetValue(style);
                _textColor.SetValue(state, MakeColor(c));
            }

            _label.Invoke(null, new[] { MakeRect(x, y, w, h), s, style });
        }
        catch { }
    }
}
