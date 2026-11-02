// SPDX-License-Identifier: MIT
// Copyright (c) 2026 sadwind227
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BatteryPet;

static class PixelIcon
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static readonly string[] Digits = { "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001", "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111" };
    public static Icon Create(int? value, int threshold, string style, bool fresh, bool charging)
    {
        // Render directly at the tray's small-icon size, rather than shrink a sparse 32px glyph.
        using var image = Render(value, threshold, style, fresh, charging, Math.Clamp(SystemInformation.SmallIconSize.Width, 16, 64));
        IntPtr handle = image.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }
    public static Bitmap Render(int? value, int threshold, string style, bool fresh, bool charging, int size)
    {
        size = Math.Clamp(size, 16, 64);
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb); image.SetResolution(96, 96);
        using var g = Graphics.FromImage(image);
        Color color = !fresh ? Color.FromArgb(160, 170, 179) : value < threshold ? Color.FromArgb(255, 112, 124) : value <= 50 ? Color.FromArgb(255, 210, 91) : Color.FromArgb(112, 244, 167);
        if (style == "数字") DrawNumber(g, value is >= 0 and <= 100 ? value : null, color, size, fresh && charging);
        else
        {
            g.ScaleTransform(size / 32f, size / 32f);
            using var brush = new SolidBrush(color); using var pen = new Pen(color, 3);
            if (style == "圆环") { g.DrawArc(Pens.DimGray, 3, 3, 25, 25, 0, 360); g.DrawArc(pen, 3, 3, 25, 25, -90, Math.Max(1, (value ?? 0) * 3.6f)); }
            else { g.DrawRectangle(pen, 3, 8, 24, 16); g.FillRectangle(brush, 6, 11, (value ?? 0) * 18 / 100, 10); g.FillRectangle(brush, 28, 13, 3, 6); }
            if (fresh && charging) g.FillRectangle(Brushes.White, 26, 0, 5, 5);
        }
        return image;
    }
    static void DrawNumber(Graphics g, int? value, Color color, int size, bool charging)
    {
        int outline = Math.Max(1, size / 24), gap = Math.Max(1, size / 12);
        int available = size - outline * 2, markerHeight = Math.Max(1, size / 16);
        int height = available - (charging ? markerHeight + outline + 1 : 0);
        string text = value?.ToString() ?? "?";
        var widths = text.Length == 3
            ? new[] { Math.Max(2, available / 7), (available - Math.Max(2, available / 7) - gap * 2) / 2, (available - Math.Max(2, available / 7) - gap * 2) / 2 }
            : Enumerable.Repeat(text.Length == 1 ? (int)(available * .64) : (available - gap) / 2, text.Length).ToArray();
        int x = (size - widths.Sum() - gap * (text.Length - 1)) / 2;
        var cells = new List<Rectangle>();
        for (int digit = 0; digit < text.Length; digit++)
        {
            int width = widths[digit];
            // A narrow leading 1 keeps 100 the same height as a two-digit percentage.
            if (text.Length == 3 && digit == 0) cells.Add(new(x, outline, width, height));
            else
            {
                string bits = text[digit] == '?' ? "111001010000010" : Digits[text[digit] - '0'];
                for (int i = 0; i < 15; i++)
                {
                    if (bits[i] != '1') continue;
                    int col = i % 3, row = i / 3;
                    int left = (int)Math.Round(col * width / 3d), right = (int)Math.Round((col + 1) * width / 3d);
                    int top = (int)Math.Round(row * height / 5d), bottom = (int)Math.Round((row + 1) * height / 5d);
                    cells.Add(new(x + left, outline + top, right - left, bottom - top));
                }
            }
            x += width + gap;
        }
        g.SmoothingMode = SmoothingMode.None; g.PixelOffsetMode = PixelOffsetMode.None;
        using var edge = new SolidBrush(Color.FromArgb(245, 19, 29, 38));
        using var ink = new SolidBrush(color);
        foreach (var cell in cells) g.FillRectangle(edge, Rectangle.Inflate(cell, outline, outline));
        foreach (var cell in cells) g.FillRectangle(ink, cell);
        if (charging)
        {
            var bar = new Rectangle(size / 3, size - outline - markerHeight, Math.Max(3, size / 3), markerHeight);
            g.FillRectangle(edge, Rectangle.Inflate(bar, outline, outline)); g.FillRectangle(Brushes.White, bar);
        }
    }
}

static class IconPreview
{
    public static void Export()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "icon-check"); Directory.CreateDirectory(folder);
        using var sheet = new Bitmap(800, 400); sheet.SetResolution(96, 96); using var g = Graphics.FromImage(sheet);
        g.Clear(Ui.Background);
        int?[] samples = { 0, 8, 18, 50, 86, 99, 100, null };
        using var label = Ui.DrawFont(9); using var title = Ui.DrawFont(12, FontStyle.Bold);
        Ui.Text(g, "像素数字托盘 · 实际尺寸与 4 倍放大", title, Ui.Ink, new(18, 6, 750, 28));
        var report = new List<string> { $"本机小图标尺寸：{SystemInformation.SmallIconSize.Width}px" };
        for (int row = 0; row < 3; row++)
        {
            int size = new[] { 16, 24, 32 }[row], y = 47 + row * 115;
            bool dark = row != 1;
            using var background = new SolidBrush(dark ? Color.FromArgb(34, 38, 44) : Color.FromArgb(239, 240, 243));
            g.FillRectangle(background, 0, y, 800, 109);
            Ui.Text(g, size + "px", label, dark ? Color.White : Ui.Ink, new(8, y + 3, 55, 25));
            for (int col = 0; col < samples.Length; col++)
            {
                using var icon = PixelIcon.Render(samples[col], 20, "数字", true, false, size);
                icon.Save(Path.Combine(folder, $"{size}-{samples[col]?.ToString() ?? "unknown"}.png"), ImageFormat.Png);
                int x = 70 + col * 91;
                g.DrawImageUnscaled(icon, x, y + 7);
                g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
                int enlarged = Math.Min(64, size * 4); g.DrawImage(icon, new Rectangle(x, y + 39, enlarged, enlarged));
                g.PixelOffsetMode = PixelOffsetMode.None;
                int top = size, bottom = -1;
                for (int py = 0; py < size; py++) for (int px = 0; px < size; px++)
                    if (icon.GetPixel(px, py).A > 0) { top = Math.Min(top, py); bottom = Math.Max(bottom, py); }
                report.Add($"{size}px · {samples[col]?.ToString() ?? "?"} · 可见高度 {bottom - top + 1}px");
            }
        }
        sheet.Save(Path.Combine(folder, "数字图标预览.png"), ImageFormat.Png);
        using var charging = PixelIcon.Render(86, 20, "数字", true, true, 16); charging.Save(Path.Combine(folder, "16-charging.png"), ImageFormat.Png);
        File.WriteAllLines(Path.Combine(folder, "检查记录.txt"), report);
    }
}
