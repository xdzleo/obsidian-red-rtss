// Builds the "Obsidian Red II" RTSS OverlayEditor layout: a static art atlas (panel, labels,
// official logos) rendered with SkiaSharp plus the .ovl that places the live values on top.
// Both come from the same coordinates below, so art and text can't drift apart.
//
// usage: ovlart <logoDir> <outDir> [K] [bearing]
//        ovlart logocheck <svg> <png> <out.png>
// The high flow NEXT serial is written as the HFN-SERIAL placeholder (install.ps1 fills it in);
// set OVLART_HFN_SERIAL to bake a real one.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args[0] == "logocheck") { LogoCheck.Run(args[1], args[2], args[3]); return; }
var logoDir = args[0];
var outDir = args[1];
if (args.Length > 2) Rtss.K = float.Parse(args[2]);
if (args.Length > 3) Rtss.Bearing = float.Parse(args[3]);
Directory.CreateDirectory(outDir);

var d = new Design(logoDir);
d.Build();
d.SaveAtlas(Path.Combine(outDir, "atlas.png"));
File.WriteAllText(Path.Combine(outDir, "ObsidianRed2.ovl"), d.Ovl(@"ObsidianRed2\atlas.png"), Encoding.Latin1);
d.SavePreview(Path.Combine(outDir, "preview.png"));
Console.WriteLine($"atlas {Design.W}x{Design.H}, {d.Layers.Count} layers, {d.Sources.Count} sources -> {outDir}");

/// Model of how RTSS places layer text: em = base font height * Size%, baseline = PositionY + K*em.
static class Rtss
{
    public const float BaseEm = 20f;       // [Master] FontHeight=-20
    public static float K = 1.02f;         // calibrated against DesktopOverlayHost captures
    public static float Bearing = 1.0f;    // left side bearing of Bahnschrift digits at 20px
    public static float Em(int size) => BaseEm * size / 100f;
}

record Source(string Name, string Units, string Format, string Formula, string Provider, string Id);

/// A run of layer text. Ovl is what RTSS evaluates, Sample is what the preview draws.
record Run(string Ovl, string Sample, int Scale = 100, string? Color = null);

sealed class Layer
{
    public required string Name;
    public string Text = "";
    public int X, Y, W, H, Origin, Size = 100;
    public string Color = "FFFFFFFF";
    public List<Run>? Runs;        // text layers only (for the preview)
    public string? Avail;          // <IF avail>runs<ELSE>--<IF>
    public (string src, float v, float max)? Bar;
    public bool Graph;
}

sealed class Design
{
    public const int W = 300, H = 456;
    const float X0 = 16, X1 = 284;
    static readonly float[] Q = { 16, 86, 160, 234 };   // metric columns (CLOCK gets the room it needs)
    const float HA = 16, HB = 160, HW = 124;             // half grid, 20px gutter
    const int MetricSize = 95, UnitScale = 55;

    // palette
    static readonly SKColor Accent = SKColor.Parse("#FF153D");
    static readonly SKColor Ink = SKColor.Parse("#F2F4F8");
    static readonly SKColor LabelC = SKColor.Parse("#7D8698");
    static readonly SKColor TagC = SKColor.Parse("#5F6779");
    const string InkHex = "FFF2F4F8", UnitHex = "FF8A93A5", AccentHex = "FFFF153D";
    const string Warm = "FFFFB224", Hot = "FFFF3B4E";

    public readonly List<Layer> Layers = new();
    public readonly List<Source> Sources = new();
    readonly SKSurface _atlas = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
    SKCanvas C => _atlas.Canvas;
    readonly string _logoDir;

    readonly SKTypeface _semiCond = Face(SKFontStyleWeight.SemiBold, SKFontStyleWidth.SemiCondensed);
    readonly SKTypeface _cond = Face(SKFontStyleWeight.SemiBold, SKFontStyleWidth.Condensed);
    readonly SKTypeface _boldCond = Face(SKFontStyleWeight.Bold, SKFontStyleWidth.SemiCondensed);
    readonly SKTypeface _rtss = Face(SKFontStyleWeight.Bold, SKFontStyleWidth.Normal); // what RTSS draws values with

    public Design(string logoDir)
    {
        _logoDir = logoDir;
        foreach (var t in new[] { _semiCond, _cond, _boldCond, _rtss })
            Console.WriteLine($"typeface {t.FamilyName} w={t.FontStyle.Weight} width={t.FontStyle.Width}");
    }

    static SKTypeface Face(SKFontStyleWeight w, SKFontStyleWidth width) =>
        SKTypeface.FromFamilyName("Bahnschrift", w, width, SKFontStyleSlant.Upright);

    static SKFont Font(SKTypeface t, float size) =>
        new(t, size) { Edging = SKFontEdging.Antialias, Subpixel = true, Hinting = SKFontHinting.Slight };

    // ------------------------------------------------------------------ build
    public void Build()
    {
        DefineSources();
        Layers.Add(new Layer { Name = "Panel art", Text = $"<I={W},{H},0,0,{W},{H}>", W = W, H = H });

        Panel();
        Performance(top: 0);
        Divider(148);
        Gpu(top: 148);
        StrongDivider(260);
        Cpu(top: 260);
        Divider(372);
        Water(top: 372);
    }

    void DefineSources()
    {
        void S(string n, string u, string f, string formula, string prov, string id) => Sources.Add(new(n, u, f, formula, prov, id));
        void Avail(string n, string prov, string id) => S(n, "", "%.0f", "validate(x)", prov, id);
        var water = "/usbhid/0C70/F012/" + (Environment.GetEnvironmentVariable("OVLART_HFN_SERIAL") ?? "HFN-SERIAL");
        S("GPU temperature", "°C", "%.0f", "", "HAL", "GPU1 temperature");
        S("GPU power", "W", "%.0f", "", "HAL", "GPU1 power");
        S("GPU usage", "%", "%.0f", "", "HAL", "GPU1 usage");
        S("GPU clock", "MHz", "%.0f", "", "HAL", "GPU1 clock");
        S("GPU memory usage", "GB", "%.1f", "x/1024", "HAL", "GPU1 memory usage");
        S("GPU total memory", "GB", "%.0f", "x/1024", "HAL", "GPU1 total memory");
        S("GPU memory percent", "%", "%.0f", "", "HAL", "GPU1 memory usage percent");
        S("GPU voltage", "V", "%.3f", "x/1000", "HAL", "GPU1 voltage");
        S("CPU temperature", "°C", "%.0f", "", "HAL", "CPU temperature");
        S("CPU power", "W", "%.0f", "", "HAL", "CPU power");
        S("CPU usage", "%", "%.0f", "", "HAL", "CPU usage");
        S("CPU clock", "MHz", "%.0f", "", "HAL", "CPU clock");
        S("RAM usage", "GB", "%.1f", "x/1024", "HAL", "RAM usage");
        S("Total RAM", "GB", "%.0f", "x/1024", "HAL", "Total RAM");
        S("RAM usage percent", "%", "%.0f", "", "HAL", "RAM usage percent");
        S("Water temperature", "°C", "%.1f", "", "LHM", water + "/temperature/0");
        S("Water flow", "l/h", "%.0f", "", "LHM", water + "/flow/0");
        Avail("GPU Temperature Available", "HAL", "GPU1 temperature");
        Avail("GPU Power Available", "HAL", "GPU1 power");
        Avail("GPU Usage Available", "HAL", "GPU1 usage");
        Avail("GPU Clock Available", "HAL", "GPU1 clock");
        Avail("GPU Memory Available", "HAL", "GPU1 memory usage");
        Avail("GPU Voltage Available", "HAL", "GPU1 voltage");
        Avail("CPU Temperature Available", "HAL", "CPU temperature");
        Avail("CPU Power Available", "HAL", "CPU power");
        Avail("CPU Usage Available", "HAL", "CPU usage");
        Avail("CPU Clock Available", "HAL", "CPU clock");
        Avail("RAM Available", "HAL", "RAM usage");
        Avail("Water Temperature Available", "LHM", water + "/temperature/0");
        Avail("Water Flow Available", "LHM", water + "/flow/0");
    }

    // ------------------------------------------------------------------ sections
    void Panel()
    {
        using var path = PanelPath(0.5f);
        using (var fill = new SKPaint { IsAntialias = true })
        {
            fill.Shader = SKShader.CreateLinearGradient(new(0, 0), new(0, H),
                new[] { SKColor.Parse("#E2151922"), SKColor.Parse("#DA0D1015"), SKColor.Parse("#D6090B0F") }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
            C.DrawPath(path, fill);
        }
        C.Save();
        C.ClipPath(path, antialias: true);
        using (var glow = new SKPaint { IsAntialias = true })
        {
            glow.Shader = SKShader.CreateRadialGradient(new(64, 58), 200,
                new[] { Accent.WithAlpha(0x2A), Accent.WithAlpha(0x0C), Accent.WithAlpha(0) }, new[] { 0f, 0.45f, 1f }, SKShaderTileMode.Clamp);
            C.DrawRect(0, 0, W, 200, glow);
        }
        using (var sheen = new SKPaint { IsAntialias = true })
        {
            sheen.Shader = SKShader.CreateLinearGradient(new(0, 0), new(0, 40),
                new[] { SKColors.White.WithAlpha(0x0E), SKColors.White.WithAlpha(0) }, null, SKShaderTileMode.Clamp);
            C.DrawRect(0, 0, W, 40, sheen);
        }
        C.Restore();
        using (var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 })
        {
            border.Shader = SKShader.CreateLinearGradient(new(0, 0), new(W, H),
                new[] { Accent.WithAlpha(0xC8), SKColors.White.WithAlpha(0x1C), SKColors.White.WithAlpha(0x12), Accent.WithAlpha(0x70) },
                new[] { 0f, 0.28f, 0.8f, 1f }, SKShaderTileMode.Clamp);
            C.DrawPath(path, border);
        }
        using var tick = new SKPaint { IsAntialias = false, Color = Accent };
        C.DrawRect(0, 0, 42, 2, tick); C.DrawRect(0, 0, 2, 30, tick);                 // top-left bracket
        C.DrawRect(W - 42, H - 2, 42, 2, tick); C.DrawRect(W - 2, H - 30, 2, 30, tick); // bottom-right bracket
    }

    static SKPath PanelPath(float inset)
    {
        const float c = 14;
        var p = new SKPath();
        p.MoveTo(inset, inset);
        p.LineTo(W - c, inset);
        p.LineTo(W - inset, c);
        p.LineTo(W - inset, H - inset);
        p.LineTo(c, H - inset);
        p.LineTo(inset, H - c);
        p.Close();
        return p;
    }

    void Performance(float top)
    {
        // wordmark
        using var wf = Font(_boldCond, 10.5f);
        var x = X0 + Tracked("OBSIDIAN", X0, top + 22, wf, Ink.WithAlpha(0xD8), 2.6f, 0) + 2.6f + 4;
        Tracked("RED", x, top + 22, wf, Accent, 2.6f, 0);
        Tracked("PERFORMANCE", X1, top + 22, Font(_cond, 9f), TagC, 1.8f, 2);

        // hero FPS + inline unit
        Text("FPS live", X0 - 1, top + 90, 180, 0, 320, InkHex, null,
            new Run("<FR>", "237"), new Run(" FPS", " FPS", 25, AccentHex));

        // 1% low / frametime, label left + value right
        const float lx = 182;
        Label("1% LOW", lx, top + 62);
        Text("1% low live", X1, top + 62, 90, 2, 100, InkHex, null, new Run("<FR10L>", "198"));
        Label("FRAME", lx, top + 90);
        Text("Frametime live", X1, top + 90, 90, 2, 100, InkHex, null,
            new Run("<FT>", "4.2"), new Run(" ms", " ms", 60, UnitHex));

        // frametime graph well
        float gy = top + 104, gh = 30;
        using var well = new SKPaint { Color = SKColors.White.WithAlpha(0x07) };
        C.DrawRect(X0, gy, X1 - X0, gh, well);
        using var axis = new SKPaint { Color = SKColors.White.WithAlpha(0x1C) };
        C.DrawRect(X0, gy + gh - 1, X1 - X0, 1, axis);
        using var mid = new SKPaint { Color = SKColors.White.WithAlpha(0x10), PathEffect = SKPathEffect.CreateDash(new[] { 2f, 3f }, 0) };
        C.DrawLine(X0, gy + gh / 2 + 0.5f, X1, gy + gh / 2 + 0.5f, mid);
        Layers.Add(new Layer { Name = "Frametime fill", Text = $"<G=Frametime,{X1 - X0:0},{gh:0},1,0,25,1>", X = (int)X0, Y = (int)gy, W = (int)(X1 - X0), H = (int)gh, Color = "30FF153D", Graph = true });
        Layers.Add(new Layer { Name = "Frametime line", Text = $"<G=Frametime,{X1 - X0:0},{gh:0},1,0,25,0>", X = (int)X0, Y = (int)gy, W = (int)(X1 - X0), H = (int)gh, Color = AccentHex, Graph = true });
    }

    void Gpu(float top)
    {
        Header(top, "nvidia-official-icon-green.svg", 14, "GEFORCE RTX 5090", "GPU");
        UsageRow(top, "GPU", ("USO", "GPU usage", "GPU Usage Available", "97", 97),
                          ("VRAM", "GPU memory percent", "GPU Memory Available", "12.4", 39),
                          new Run("/%GPU total memory% GB", "/32 GB", 60, UnitHex), "GPU memory usage");
        var y = top + 76; var v = top + 96;
        Metric("TEMP", 0, y, v, "GPU temperature", "GPU Temperature Available", "64", "°C", "88", Temp("GPU temperature", 70, 83));
        Metric("CONSUMO", 1, y, v, "GPU power", "GPU Power Available", "412", " W", "575");
        Metric("CLOCK", 2, y, v, "GPU clock", "GPU Clock Available", "2850", " MHz", "3000");
        Metric("VOLT", 3, y, v, "GPU voltage", "GPU Voltage Available", "1.050", " V", "1.100");
    }

    void Cpu(float top)
    {
        Header(top, "amd-official-icon-white.svg", 12.5f, "RYZEN 7 9800X3D", "CPU");
        UsageRow(top, "CPU", ("USO", "CPU usage", "CPU Usage Available", "23", 23),
                          ("RAM", "RAM usage percent", "RAM Available", "16.1", 50),
                          new Run("/%Total RAM% GB", "/32 GB", 60, UnitHex), "RAM usage");
        var y = top + 76; var v = top + 96;
        Metric("TEMP", 0, y, v, "CPU temperature", "CPU Temperature Available", "61", "°C", "95", Temp("CPU temperature", 75, 88));
        Metric("CONSUMO", 1, y, v, "CPU power", "CPU Power Available", "78", " W", "162");
        Metric("CLOCK", 2, y, v, "CPU clock", "CPU Clock Available", "5215", " MHz", "5450");
    }

    void Water(float top)
    {
        Header(top, "aquacomputer-emblem.svg", 15.5f, "HIGH FLOW NEXT", "LOOP");
        var y = top + 48; var v = top + 68;
        Metric("ÁGUA", -1, y, v, "Water temperature", "Water Temperature Available", "23.6", "°C", "45.0", Temp("Water temperature", 35, 40));
        Metric("VAZÃO", -2, y, v, "Water flow", "Water Flow Available", "209", " l/h", "350");
    }

    // ------------------------------------------------------------------ building blocks
    void Header(float top, string logo, float logoH, string name, string tag)
    {
        float baseline = top + 24;
        using var nf = Font(_semiCond, 13.5f);
        float capMid = baseline - nf.Metrics.CapHeight / 2;
        DrawLogo(logo, X0, capMid, logoH);
        Tracked(name, 44, baseline, nf, Ink, 0.7f, 0);
        Tracked(tag, X1, baseline, Font(_cond, 9f), TagC, 1.8f, 2);
    }

    void UsageRow(float top, string who, (string label, string src, string avail, string sample, float pct) a,
                  (string label, string src, string avail, string sample, float pct) b, Run bSuffix, string bValueSrc)
    {
        float baseline = top + 48, barY = baseline + 6;
        Label(a.label, HA, baseline);
        Text($"{who} usage live", HA + HW, baseline, 80, 2, 100, InkHex, a.avail,
            new Run($"%{a.src}%", a.sample), new Run(" %", " %", 60, UnitHex));
        Bar(HA, barY, a.src, a.pct);
        Label(b.label, HB, baseline);
        Text($"{b.label} live", HB + HW, baseline, 100, 2, 100, InkHex, b.avail,
            new Run($"%{bValueSrc}%", b.sample), bSuffix);
        Bar(HB, barY, b.src, b.pct);
    }

    /// col >= 0: metric column Q[col]; -1/-2: left/right half.
    void Metric(string label, int col, float labelBase, float valueBase, string src, string avail, string sample, string unit, string worst, string? color = null)
    {
        float x = col >= 0 ? Q[col] : col == -1 ? HA : HB;
        float right = col >= 0 ? (col + 1 < Q.Length ? Q[col + 1] - 6 : X1 + 4) : col == -1 ? HA + HW : X1;
        Label(label, x, labelBase);
        Text($"{src} live", x - 1, valueBase, (int)(right - x + 1), 0, MetricSize, color ?? InkHex, avail,
            new Run($"%{src}%", sample), new Run(unit, unit, UnitScale, UnitHex));
        float em = Rtss.Em(MetricSize);
        float w = Font(_rtss, em).MeasureText(worst) + Font(_rtss, em * UnitScale / 100f).MeasureText(unit);
        Console.WriteLine($"{(w > right - x ? "WARN" : "ok  ")} {label,-8} worst '{worst}{unit}' = {w:0.0}px of {right - x:0}px");
    }

    static string Temp(string src, int warm, int hot)
    {
        if (Environment.GetEnvironmentVariable("OVLART_COLORTEST") == "1")   // verification build only
            (warm, hot) = src.StartsWith("GPU") ? (20, 24) : src.StartsWith("Water") ? (20, 30) : (warm, hot);
        return $"{InkHex},,{warm},{Warm},{warm},{hot},{Hot},{hot},({src},0)";
    }

    void Bar(float x, float y, string src, float samplePct)
    {
        using var track = new SKPaint { Color = SKColors.White.WithAlpha(0x17) };
        C.DrawRect(x, y, HW, 3, track);
        Layers.Add(new Layer { Name = $"{src} bar", Text = $"<G={src},{HW:0},3,0,0,100,8>", X = (int)x, Y = (int)y, W = (int)HW, H = 3, Color = AccentHex, Bar = (src, samplePct, 100) });
    }

    void Divider(float y)
    {
        using var line = new SKPaint { Color = SKColors.White.WithAlpha(0x14) };
        C.DrawRect(X0, y, X1 - X0, 1, line);
        using var notch = new SKPaint { Color = Accent.WithAlpha(0xB0) };
        C.DrawRect(X0, y, 12, 1, notch);
    }

    /// Edge-to-edge 2px accent bar with a soft glow: the GPU | CPU split.
    void StrongDivider(float y)
    {
        using var clip = PanelPath(1f);
        C.Save();
        C.ClipPath(clip, antialias: true);
        using (var glow = new SKPaint { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f) })
        {
            glow.Shader = SKShader.CreateLinearGradient(new(0, 0), new(W, 0),
                new[] { Accent.WithAlpha(0x70), Accent.WithAlpha(0x28), Accent.WithAlpha(0x00) }, new[] { 0f, 0.55f, 1f }, SKShaderTileMode.Clamp);
            C.DrawRect(0, y - 2, W, 6, glow);
        }
        using (var bar = new SKPaint { IsAntialias = false })
        {
            bar.Shader = SKShader.CreateLinearGradient(new(0, 0), new(W, 0),
                new[] { Accent, Accent.WithAlpha(0xC0), SKColors.White.WithAlpha(0x40) }, new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
            C.DrawRect(0, y, W, 2, bar);
        }
        C.Restore();
    }

    void Label(string text, float x, float baseline) => Tracked(text, x, baseline, Font(_cond, 10f), LabelC, 1.3f, 0);

    /// Text layer anchored on a baseline. align 0 = left edge at x, 2 = right edge at x.
    void Text(string name, float x, float baseline, int width, int align, int size, string color, string? avail, params Run[] runs)
    {
        float em = Rtss.Em(size);
        // nested <S=-n> is a percentage of the base font, not of the layer Size (measured in RTSS 7.3.5)
        var body = string.Concat(runs.Select(r => r.Scale == 100 && r.Color == null ? r.Ovl
            : $"<S=-{(int)MathF.Round(r.Scale * size / 100f)}>{(r.Color != null ? $"<C={r.Color}>" : "")}{r.Ovl}{(r.Color != null ? "<C>" : "")}<S>"));
        var text = avail != null ? $"<IF {avail}>{body}<ELSE>--<IF>" : body;
        int px = align == 2 ? (int)MathF.Round(x - width) : (int)MathF.Round(x);
        Layers.Add(new Layer
        {
            Name = name, Text = text, X = px, Y = (int)MathF.Round(baseline - Rtss.K * em), W = width,
            H = (int)MathF.Ceiling(em * 1.4f), Origin = align, Size = size, Color = color, Runs = runs.ToList(), Avail = avail,
        });
    }

    float Tracked(string text, float x, float baseline, SKFont f, SKColor col, float track, int align)
    {
        float w = text.Sum(ch => f.MeasureText(ch.ToString())) + track * (text.Length - 1);
        float sx = align == 2 ? x - w : align == 1 ? x - w / 2 : x;
        using var p = new SKPaint { IsAntialias = true, Color = col };
        foreach (var ch in text)
        {
            var s = ch.ToString();
            C.DrawText(s, sx, baseline, f, p);
            sx += f.MeasureText(s) + track;
        }
        return w;
    }

    void DrawLogo(string file, float x, float centerY, float height)
    {
        var (vb, shapes) = SvgLite.Load(Path.Combine(_logoDir, file));
        float s = height / vb.Height;
        C.Save();
        C.Translate(x, centerY - height / 2);
        C.Scale(s);
        C.Translate(-vb.Left, -vb.Top);
        foreach (var sh in shapes)
        {
            using var p = new SKPaint { IsAntialias = true, Color = sh.Fill };
            C.DrawPath(sh.Path, p);
        }
        C.Restore();
        Console.WriteLine($"logo {file}: {vb.Width * s:0.0}x{height:0.0} at {x},{centerY - height / 2:0.0}");
    }

    // ------------------------------------------------------------------ output
    public void SaveAtlas(string file)
    {
        using var img = _atlas.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(file, data.ToArray());
    }

    public string Ovl(string atlasPath)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append("\r\n");
        L("[Master]"); L("Implementation=2"); L("FontFace=Bahnschrift"); L("FontHeight=-20"); L("FontWeight=700"); L("ZoomRatio=1");
        L("[Settings]"); L("Name=Obsidian Red II"); L("RefreshPeriod=500"); L("LockUserSettings=1"); L($"EmbeddedImage={atlasPath}"); L("PingAddr=");
        L("[General]"); L($"Sources={Sources.Count}"); L("Tables=0"); L($"Layers={Layers.Count}");
        for (int i = 0; i < Sources.Count; i++)
        {
            var s = Sources[i];
            L($"[Source{i}]"); L($"Name={s.Name}"); L($"Units={s.Units}"); L($"Format={s.Format}"); L($"Formula={s.Formula}"); L($"Provider={s.Provider}"); L($"ID={s.Id}");
        }
        for (int i = 0; i < Layers.Count; i++)
        {
            var l = Layers[i];
            L($"[Layer{i}]"); L($"Name={l.Name}"); L($"Text={l.Text}"); L($"PositionX={l.X}"); L($"PositionY={l.Y}"); L($"ExtentX={l.W}"); L($"ExtentY={l.H}");
            L($"ExtentOrigin={l.Origin}"); L("FixedAlignment=1"); L($"Size={l.Size}"); L($"TextColor={l.Color}");
        }
        return sb.ToString();
    }

    /// Approximation of the RTSS render over a fake game frame, using the same placement model.
    public void SavePreview(string file)
    {
        const int pad = 24;
        using var surf = SKSurface.Create(new SKImageInfo(W + pad * 2, H + pad * 2));
        var c = surf.Canvas;
        using (var bg = new SKPaint())
        {
            bg.Shader = SKShader.CreateLinearGradient(new(0, 0), new(W, H + pad * 2),
                new[] { SKColor.Parse("#6F93B8"), SKColor.Parse("#C7A77C"), SKColor.Parse("#5C5340"), SKColor.Parse("#2E3328") },
                new[] { 0f, 0.42f, 0.6f, 1f }, SKShaderTileMode.Clamp);
            c.DrawRect(0, 0, W + pad * 2, H + pad * 2, bg);
        }
        c.Translate(pad, pad);
        using (var img = _atlas.Snapshot()) c.DrawImage(img, 0, 0);
        var rnd = new Random(7);
        foreach (var l in Layers)
        {
            if (l.Bar is { } b)
            {
                using var p = new SKPaint { Color = SKColor.Parse(l.Color) };
                c.DrawRect(l.X, l.Y, l.W * b.v / b.max, l.H, p);
            }
            else if (l.Graph)
            {
                using var path = new SKPath();
                float baseY = l.Y + l.H;
                for (int i = 0; i <= l.W; i++)
                {
                    float ms = 4.2f + (float)(rnd.NextDouble() - 0.5) * 0.9f + (i % 61 == 0 ? 7 : 0);
                    float yy = baseY - ms / 25f * l.H;
                    if (i == 0) path.MoveTo(l.X + i, yy); else path.LineTo(l.X + i, yy);
                }
                bool fill = l.Name.Contains("fill");
                if (fill) { path.LineTo(l.X + l.W, baseY); path.LineTo(l.X, baseY); path.Close(); }
                using var p = new SKPaint { IsAntialias = true, Color = SKColor.Parse(l.Color), Style = fill ? SKPaintStyle.Fill : SKPaintStyle.Stroke, StrokeWidth = 1 };
                c.DrawPath(path, p);
            }
            else if (l.Runs != null)
            {
                float em = Rtss.Em(l.Size), baseline = l.Y + Rtss.K * em;
                var color = SKColor.Parse(l.Color.Split(',')[0]);
                var fonts = l.Runs.Select(r => Font(_rtss, em * r.Scale / 100f)).ToList();
                float w = l.Runs.Select((r, i) => fonts[i].MeasureText(r.Sample)).Sum();
                float x = l.Origin == 2 ? l.X + l.W - w : l.X + Rtss.Bearing;
                for (int i = 0; i < l.Runs.Count; i++)
                {
                    using var p = new SKPaint { IsAntialias = true, Color = l.Runs[i].Color is { } rc ? SKColor.Parse(rc) : color };
                    c.DrawText(l.Runs[i].Sample, x, baseline, fonts[i], p);
                    x += fonts[i].MeasureText(l.Runs[i].Sample);
                }
                if (l.Origin == 0 && x > l.X + l.W) Console.WriteLine($"warn: '{l.Name}' sample overflows extent by {x - l.X - l.W:0.0}px");
            }
        }
        using var snap = surf.Snapshot();
        using var data = snap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(file, data.ToArray());
    }
}

/// Original PNG vs. vector render (big, difference map, and small sizes on dark).
static class LogoCheck
{
    public static void Run(string svg, string png, string outFile)
    {
        var (vb, shapes) = SvgLite.Load(svg);
        using var orig = SKBitmap.Decode(png);
        int S = orig.Width;
        using var vec = new SKBitmap(S, S, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var vc = new SKCanvas(vec))
        {
            vc.Clear(SKColors.Transparent);
            vc.Translate(vb.Left * 0, 0); // the SVG viewBox is in the PNG's own pixel space
            foreach (var sh in shapes) { using var p = new SKPaint { IsAntialias = true, Color = sh.Fill }; vc.DrawPath(sh.Path, p); }
        }
        // alpha difference
        double diff = 0, total = 0;
        using var dm = new SKBitmap(S, S);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var a = orig.GetPixel(x, y); var b = vec.GetPixel(x, y);
                int da = Math.Abs(a.Alpha - b.Alpha);
                bool hueA = a.Blue > a.Red, hueB = b.Blue > b.Red;
                int dc = (a.Alpha > 128 && b.Alpha > 128 && hueA != hueB) ? 255 : 0;
                diff += Math.Max(da, dc); total += Math.Max(a.Alpha, b.Alpha);
                dm.SetPixel(x, y, new SKColor((byte)Math.Max(da, dc), 0, 0, 255));
            }
        Console.WriteLine($"mismatch: {100 * diff / total:0.00}% of shape alpha");
        using var surf = SKSurface.Create(new SKImageInfo(S * 3 + 200, S));
        var c = surf.Canvas;
        c.Clear(SKColors.White);
        c.DrawBitmap(orig, 0, 0);
        c.DrawBitmap(vec, S, 0);
        c.DrawBitmap(dm, S * 2, 0);
        using var dark = new SKPaint { Color = SKColor.Parse("#12151B") };
        c.DrawRect(S * 3, 0, 200, S, dark);
        float y0 = 10;
        foreach (var h in new[] { 16f, 24f, 32f, 64f })
        {
            c.Save(); c.Translate(S * 3 + 10, y0); c.Scale(h / vb.Height); c.Translate(-vb.Left, -vb.Top);
            foreach (var sh in shapes) { using var p = new SKPaint { IsAntialias = true, Color = sh.Fill }; c.DrawPath(sh.Path, p); }
            c.Restore();
            using (var o = SKImage.FromBitmap(orig))
                c.DrawImage(o, SKRect.Create(vb.Left / S * 0, 0, S, S), SKRect.Create(S * 3 + 100, y0 - vb.Top / vb.Height * h, h * S / vb.Height, h * S / vb.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            y0 += h + 12;
        }
        using var img = surf.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(outFile, data.ToArray());
    }
}

static class SvgLite
{
    public record Shape(SKPath Path, SKColor Fill);

    public static (SKRect vb, List<Shape> shapes) Load(string file)
    {
        var s = File.ReadAllText(file);
        float[] Nums(string v) => Regex.Matches(v, @"-?\d*\.?\d+(?:e-?\d+)?").Select(m => float.Parse(m.Value)).ToArray();
        var v = Nums(Regex.Match(s, "viewBox=\"([^\"]+)\"").Groups[1].Value);
        var classFill = Regex.Matches(s, @"\.([\w-]+)\s*\{\s*fill:\s*(#[0-9a-fA-F]{3,8})")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var shapes = new List<Shape>();
        foreach (Match m in Regex.Matches(s, @"<(path|polygon)\b([^>]*)>"))
        {
            var attrs = m.Groups[2].Value;
            string? A(string n) { var mm = Regex.Match(attrs, $@"(?<![\w-]){n}=""([^""]*)"""); return mm.Success ? mm.Groups[1].Value : null; }
            SKPath p;
            if (m.Groups[1].Value == "path") p = SKPath.ParseSvgPathData(A("d")!);
            else
            {
                var n = Nums(A("points")!);
                p = new SKPath();
                p.MoveTo(n[0], n[1]);
                for (int i = 2; i + 1 < n.Length; i += 2) p.LineTo(n[i], n[i + 1]);
                p.Close();
            }
            if (A("fill-rule") == "evenodd") p.FillType = SKPathFillType.EvenOdd;
            if (A("transform") is { } t && Regex.Match(t, @"translate\(\s*([-\d.]+)[\s,]+([-\d.]+)\s*\)") is { Success: true } tm)
            {
                var m2 = SKMatrix.CreateTranslation(float.Parse(tm.Groups[1].Value), float.Parse(tm.Groups[2].Value));
                p.Transform(in m2);
            }
            var fill = A("fill") ?? Regex.Match(A("style") ?? "", @"fill:\s*(#[0-9a-fA-F]+)").Groups[1].Value;
            if (string.IsNullOrEmpty(fill) && A("class") is { } cls && classFill.TryGetValue(cls, out var cf)) fill = cf;
            shapes.Add(new Shape(p, SKColor.Parse(string.IsNullOrEmpty(fill) ? "#000000" : fill)));
        }
        return (SKRect.Create(v[0], v[1], v[2], v[3]), shapes);
    }
}
