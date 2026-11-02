// SPDX-License-Identifier: MIT
using System.Drawing.Imaging;

namespace BatteryPet;

static class EdgeChecks
{
    public static IEnumerable<string> Run(string folder)
    {
        var results = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); results.Add(message); }
        var visible = new Rectangle(54, 16, 125, 147);
        var desktop = new Rectangle(0, 0, 1920, 1040);
        var leftTop = PetBounds.ConstrainVisible(new(-5000, -5000), visible, desktop, 17);
        var rightBottom = PetBounds.ConstrainVisible(new(5000, 5000), visible, desktop, 17);
        Check(leftTop == new Point(17 - visible.Left, -visible.Top), "透明窗口可越界，角色左侧预留滚动条且顶部贴边");
        Check(rightBottom == new Point(1903 - visible.Right, 1040 - visible.Bottom), "角色右侧预留滚动条且底部贴任务栏上方");
        Check(PetBounds.ConstrainVisible(leftTop, visible, desktop, 17) == leftTop, "贴边位置重复约束不发生抖动");
        Check(PetBounds.ConstrainVisible(new(400, 300), visible, desktop, 17) == new Point(400, 300), "新边界规则保留桌面内部位置");
        var restored = System.Text.Json.JsonSerializer.Deserialize<Preferences>("{\"X\":-1800,\"Y\":-120,\"PetPositionSaved\":true}")!;
        Check(restored.X == -1800 && restored.Y == -120 && restored.PetPositionSaved, "保存位置兼容负坐标显示器");
        foreach (var area in new[] { desktop, new Rectangle(40, 0, 1880, 1080), new Rectangle(0, 0, 1880, 1080), new Rectangle(-1920, -200, 1920, 1040) })
        foreach (var desired in new[] { new Point(-10000, -10000), new Point(10000, -10000), new Point(-10000, 10000), new Point(10000, 10000) })
        {
            var origin = PetBounds.ConstrainVisible(desired, visible, area, 17);
            var pet = new Rectangle(origin.X + visible.X, origin.Y + visible.Y, visible.Width, visible.Height);
            Check(area.Contains(pet) && pet.Left >= area.Left + 17 && pet.Right <= area.Right - 17, "四角/侧面任务栏/副屏约束 " + area + " " + desired);
            var bubble = PetBounds.PlaceBubble(pet, new(258, 150), area);
            Check(area.Contains(bubble), "四角气泡完整位于当前屏幕工作区 " + area + " " + desired);
        }
        var upper = PetBounds.PlaceBubble(new(500, 0, 100, 140), new(258, 150), desktop);
        var lower = PetBounds.PlaceBubble(new(500, 850, 100, 140), new(258, 150), desktop);
        Check(upper.Top == 140 && lower.Bottom == 850, "气泡上方优先，顶部空间不足时置于角色下方");
        var tiny = PetBounds.PlaceBubble(new(0, 0, 50, 50), new(258, 150), new(0, 0, 80, 60));
        Check(new Rectangle(0, 0, 80, 60).Contains(tiny) && Math.Abs(tiny.Width / 258.0 - tiny.Height / 150.0) < .01, "极小工作区气泡等比缩小且不越界");

        using var source = new Bitmap(300, 300);
        using (var g = Graphics.FromImage(source)) g.FillEllipse(Brushes.Coral, 100, 30, 80, 245);
        using var bytes = new MemoryStream(); source.Save(bytes, ImageFormat.Png);
        using var custom = new PetImage(bytes.ToArray());
        foreach (var dpi in new[] { 96, 144, 192 })
        foreach (var percent in new[] { 60, 100, 180 })
        {
            var size = new Size(240 * percent * dpi / 9600, 180 * percent * dpi / 9600);
            var bounds = custom.GetVisibleBounds(size, true);
            Check(bounds.Width < size.Width / 2 && bounds.Top > 0, "自定义图片忽略透明留白 " + percent + "% " + dpi + "DPI");
            Check(custom.GetVisibleBounds(size, true) == bounds, "可见范围缓存稳定 " + percent + "% " + dpi + "DPI");
            using var bitmap = new Bitmap(size.Width, size.Height);
            using var g = Graphics.FromImage(bitmap);
            foreach (double time in new[] { 0.0, Math.PI / 4.4, 3 * Math.PI / 4.4 })
            {
                g.Clear(Color.Transparent); custom.Draw(g, new(Point.Empty, size), time, true);
                Check(bounds.Contains(VisiblePixels.Measure(bitmap)), "呼吸动画未越过缓存范围 " + percent + "% " + dpi + "DPI " + time);
            }
        }
        string sample = Path.Combine(AppContext.BaseDirectory, "Examples", "CustomPet", "batterypet-pet.png");
        using var samplePet = File.Exists(sample) ? PetImage.Load(sample) : new PetImage(bytes.ToArray());
        foreach (bool builtin in new[] { true, false })
        {
            const int w = 800, h = 600;
            using var sheet = new Bitmap(w * 2, h * 2);
            sheet.SetResolution(96, 96);
            using var g = Graphics.FromImage(sheet); g.Clear(Color.FromArgb(225, 240, 240));
            var size = new Size(360, 270);
            var bounds = builtin ? PetWindow.MeasureBuiltin(size, true) : samplePet.GetVisibleBounds(size, true);
            for (int i = 0; i < 4; i++)
            {
                int px = i % 2 * w, py = i / 2 * h;
                var area = new Rectangle(0, 0, w, h - 32);
                var origin = PetBounds.ConstrainVisible(new(i % 2 == 0 ? -5000 : 5000, i < 2 ? -5000 : 5000), bounds, area, 26);
                var pet = new Rectangle(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height);
                using var image = new Bitmap(size.Width, size.Height);
                image.SetResolution(96, 96);
                using (var pg = Graphics.FromImage(image))
                {
                    if (builtin) PetRenderer.Draw(pg, new(Point.Empty, size), "薄荷海", Math.PI / 4.4, false, false, 0, PointF.Empty);
                    else samplePet.Draw(pg, new(Point.Empty, size), Math.PI / 4.4, true);
                }
                var clip = g.Save(); g.SetClip(new Rectangle(px, py, w, h));
                g.FillRectangle(Brushes.LightSlateGray, px, py + h - 32, w, 32);
                g.DrawImageUnscaled(image, px + origin.X, py + origin.Y);
                var bubble = PetBounds.PlaceBubble(pet, new(387, 225), area);
                using var window = new BatteryBubble(() => new("布局检查 · 示例设备", 86, true, false, false, "非真实设备读数", true));
                window.Size = bubble.Size;
                using var bubbleImage = window.RenderBitmap();
                g.DrawImageUnscaled(bubbleImage, px + bubble.X, py + bubble.Y);
                g.DrawRectangle(Pens.Teal, px + 26, py, w - 53, h - 33);
                g.Restore(clip);
            }
            sheet.Save(Path.Combine(folder, builtin ? "贴边与气泡-内置.png" : "贴边与气泡-自定义.png"), ImageFormat.Png);
        }
        Check(true, "已保存内置角色与自定义角色四角气泡布局截图（150% 缩放）");
        return results.Select(s => "边界 · " + s);
    }
}
