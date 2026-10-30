using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Gloam.Tests;

/// <summary>
/// Composes the side-by-side comparison figures used in the review documents.
/// </summary>
/// <remarks>
/// Not a test: no assertions, and it returns immediately unless <c>GLOAM_FIGURES=1</c>. It exists so
/// the figures in a report can be regenerated from the current binaries instead of being assembled by
/// hand, which is how documentation images end up showing a version nobody ships any more.
///
/// A callout is a rectangle in source coordinates plus a caption. Boxes are drawn on both halves at
/// the same coordinates, so a reader can see that the same control changed language rather than
/// comparing two arbitrary crops.
/// </remarks>
public sealed class DocumentationFigures
{
    private const double Padding = 24;
    private const double CaptionHeight = 34;
    private const double LegendLineHeight = 22;

    [Fact]
    public void Compose_comparison_figures()
    {
        if (Environment.GetEnvironmentVariable("GLOAM_FIGURES") != "1")
            return;

        // WPF imaging types require an STA thread; WpfTestHost owns the one this suite uses.
        WpfTestHost.Run(Compose);
    }

    private static void Compose()
    {
        string shots = Environment.GetEnvironmentVariable("GLOAM_SHOTS_DIR")
            ?? Path.Combine(Path.GetTempPath(), "gloam-ui");
        string output = Environment.GetEnvironmentVariable("GLOAM_FIGURES_DIR")
            ?? Path.Combine(Path.GetTempPath(), "gloam-figures");
        Directory.CreateDirectory(output);

        Zoom(shots, output);

        Compare(shots, output, "fig1-dashboard",
            "dashboard-en-US-full.png", "dashboard-zh-Hans-full.png",
            "Original · English (en-US)", "Localized · 简体中文 (zh-Hans)",
            new[]
            {
                new Callout(892, 12, 60, 32, "标题栏语言按钮：显示的是「将切换到的语言」，英文界面显示 ZH"),
                new Callout(492, 58, 570, 44, "页头：夜间配置、刷新、夜间模式状态"),
                new Callout(24, 142, 326, 250, "显示器卡片：字段名与按钮翻译；Gamma 2.4 / 100% / 6500K 为数值与单位，保持原样"),
                new Callout(366, 142, 320, 250, "自动禁用卡片：标题、说明、占位符、按钮"),
                new Callout(24, 480, 1032, 180, "Game Lab 卡片：模式状态、正在游玩、游戏库操作"),
            });

        Compare(shots, output, "fig2-gamelab",
            "gamelab-en-US-full.png", "gamelab-zh-Hans-full.png",
            "Original · English (en-US)", "Localized · 简体中文 (zh-Hans)",
            new[]
            {
                new Callout(866, 76, 66, 36, "主操作按钮：Apply → 应用（原先硬编码，本轮迁移）"),
                new Callout(214, 574, 48, 34, "添加游戏：Add → 添加"),
                new Callout(18, 650, 252, 30, "库扫描说明整段翻译；Steam / Epic / GOG / Xbox 为平台名，保留原文"),
                new Callout(298, 360, 752, 140, "高级面板：字段名与下拉选项（Not detected / Automatic / Game window display(s)）全部翻译"),
                new Callout(770, 686, 288, 24, "底部快捷键提示：F4 动作按原文语义翻译"),
            });

        Compare(shots, output, "fig3-settings",
            "settings-en-US-full.png", "settings-zh-Hans-full.png",
            "Original · English (en-US)", "Localized · 简体中文 (zh-Hans)",
            new[]
            {
                new Callout(16, 123, 566, 105, "界面语言：跟随系统 / English / 中文（简体）。切换在本窗口内即时生效，不重启"),
                new Callout(16, 244, 566, 88, "伽马：Gamma 2.4 为行业规格写法，两种语言一致"),
                new Callout(16, 347, 566, 175, "亮度：模式说明、预设百分比、增强暗部细节"),
                new Callout(16, 536, 566, 200, "色温与色调：暖 / 冷、绿 / 洋红，数值 0 保留"),
                new Callout(16, 754, 566, 150, "夜间模式覆盖：独立于全局设置的偏移量"),
                new Callout(238, 950, 348, 44, "底部操作：对比（按住）、全部重置、保存并关闭"),
            });

        Compare(shots, output, "fig4-pseudo",
            "gamelab-en-US-full.png", "gamelab-qps-ploc-full.png",
            "English (en-US) · 基准宽度", "伪语言 qps-ploc · 文本 +40%",
            new[]
            {
                new Callout(298, 360, 752, 140, "同一面板：字段名加长 40% 后仍完整显示，未越界、未截断"),
                new Callout(18, 650, 252, 30, "多行说明改为换行，不出现省略号"),
                new Callout(24, 128, 1032, 52, "游戏模式说明行改为换行显示（本轮修复：原先固定 265px 列会截断）"),
            });
    }

    /// <summary>Enlarged crops kept for reviewing a specific control during the audit.</summary>
    private static void Zoom(string shots, string output)
    {
        Write(ZoomBitmap(Path.Combine(shots, "gamelab-zh-Hans.png"), new Int32Rect(455, 372, 610, 62), 3),
            Path.Combine(output, "zoom-gamelab-advanced-zh.png"));

        Write(ZoomBitmap(Path.Combine(shots, "gamelab-qps-ploc.png"), new Int32Rect(300, 120, 760, 90), 2),
            Path.Combine(output, "zoom-gamelab-state-qps.png"));
    }

    private static void Compare(string shots, string output, string name,
        string leftFile, string rightFile, string leftCaption, string rightCaption, Callout[] callouts)
    {
        BitmapSource left = Load(Path.Combine(shots, leftFile));
        BitmapSource right = Load(Path.Combine(shots, rightFile));

        double totalWidth = left.PixelWidth + right.PixelWidth + Padding * 3;
        double bodyHeight = Math.Max(left.PixelHeight, right.PixelHeight);
        double totalHeight = Padding * 2 + CaptionHeight + bodyHeight + Padding + LegendLineHeight * (callouts.Length + 1);

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), null,
                new Rect(0, 0, totalWidth, totalHeight));

            double leftX = Padding;
            double rightX = Padding * 2 + left.PixelWidth;
            double top = Padding + CaptionHeight;

            DrawCaption(dc, leftCaption, leftX, top - CaptionHeight + 6, left.PixelWidth);
            DrawCaption(dc, rightCaption, rightX, top - CaptionHeight + 6, right.PixelWidth);

            dc.DrawImage(left, new Rect(leftX, top, left.PixelWidth, left.PixelHeight));
            dc.DrawImage(right, new Rect(rightX, top, right.PixelWidth, right.PixelHeight));

            var border = new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xDC, 0xE3)), 1);
            dc.DrawRectangle(null, border, new Rect(leftX, top, left.PixelWidth, left.PixelHeight));
            dc.DrawRectangle(null, border, new Rect(rightX, top, right.PixelWidth, right.PixelHeight));

            double legendTop = top + bodyHeight + Padding - 6;
            for (int i = 0; i < callouts.Length; i++)
            {
                Callout callout = callouts[i];
                DrawCallout(dc, callout, leftX, top);
                DrawCallout(dc, callout, rightX, top);
                DrawLegend(dc, i + 1, callout.Caption, Padding, legendTop + i * LegendLineHeight);
            }
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(totalWidth), (int)Math.Ceiling(totalHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        Write(bitmap, Path.Combine(output, $"{name}.png"));
    }

    private static void DrawCallout(DrawingContext dc, Callout callout, double originX, double originY)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xEE, 0x5B, 0x36));
        var rect = new Rect(originX + callout.X, originY + callout.Y, callout.Width, callout.Height);
        dc.DrawRectangle(null, new Pen(accent, 2), rect);
    }

    private static void DrawLegend(DrawingContext dc, int index, string caption, double x, double y)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xEE, 0x5B, 0x36));
        dc.DrawEllipse(accent, null, new Point(x + 9, y + 8), 9, 9);

        var number = Text(index.ToString(CultureInfo.InvariantCulture), 10, Brushes.White, bold: true);
        dc.DrawText(number, new Point(x + 9 - number.Width / 2, y + 8 - number.Height / 2));

        dc.DrawText(Text(caption, 11, new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x27))),
            new Point(x + 26, y));
    }

    private static void DrawCaption(DrawingContext dc, string caption, double x, double y, double width)
    {
        dc.DrawText(Text(caption, 12, new SolidColorBrush(Color.FromRgb(0x66, 0x6C, 0x78)), bold: true),
            new Point(x, y));
        _ = width;
    }

    private static FormattedText Text(string value, double size, Brush brush, bool bold = false)
        => new(
            value,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei, Segoe UI"), FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            size,
            brush,
            1.0);

    private static BitmapSource ZoomBitmap(string path, Int32Rect region, int scale)
    {
        BitmapSource source = Load(path);
        var cropped = new CroppedBitmap(source, region);

        var scaled = new TransformedBitmap(cropped, new ScaleTransform(scale, scale));
        var frozen = new WriteableBitmap(scaled);
        frozen.Freeze();
        return frozen;
    }

    private static BitmapSource Load(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void Write(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private readonly record struct Callout(double X, double Y, double Width, double Height, string Caption);
}
