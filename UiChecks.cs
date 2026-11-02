using System.Drawing.Imaging;
using System.Text.Json;
using System.Text;

namespace BatteryPet;

static class UiChecks
{
    public static void Run()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "ui-check"); Directory.CreateDirectory(folder);
        var checks = new List<string>();
        void Require(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
        try
        {
            var gesture = new PetInteraction();
            Require(!gesture.BubbleVisible(0), "启动不显示电量气泡");
            var desktop = new Rectangle(0, 0, 1920, 1040); var petSize = new Size(360, 270);
            Require(PetBounds.Constrain(new(-500, -500), petSize, desktop) == new Point(0, 0), "拖动不能越过桌面左上边界");
            Require(PetBounds.Constrain(new(2000, 1200), petSize, desktop) == new Point(1560, 770), "拖动不能越过右下边界或进入任务栏");
            Require(PetBounds.Constrain(new(400, 300), petSize, desktop) == new Point(400, 300), "桌面内部拖动位置不改变");
            Require(PetBounds.Constrain(new(-2000, 900), petSize, new(-1920, 0, 1920, 1040)) == new Point(-1920, 770), "副屏负坐标边界正确限制");
            Require(PetBounds.Constrain(new(200, 200), new(2000, 1200), desktop) == Point.Empty, "超大角色限制在工作区起点而不抛异常");
            gesture.Down(new(100, 100)); gesture.Move(new(102, 101), 4);
            Require(gesture.Up(1, 6), "容许微小手抖，仍判定为点击");
            Require(gesture.BubbleVisible(6.99) && !gesture.BubbleVisible(7), "电量气泡到期自动收起");
            gesture.Down(new(100, 100)); Require(gesture.Move(new(120, 100), 4), "越过阈值进入拖动");
            Require(!gesture.Up(2, 6) && !gesture.BubbleVisible(2), "拖动松手不显示电量");
            gesture.Down(new(100, 100)); gesture.Cancel(); Require(!gesture.Up(3, 6), "失去鼠标捕获后不误触点击");
            gesture.Down(new(100, 100)); gesture.Up(10, 3); gesture.Dismiss(); Require(!gesture.BubbleVisible(10), "隐藏/手动关闭后气泡消失");
            var prefs = new Preferences(); var json = JsonSerializer.Serialize(new Preferences { PetSize = 150, PetTheme = "暮光紫", BubbleSeconds = 9, PetAnimate = false });
            var restored = JsonSerializer.Deserialize<Preferences>(json)!;
            Require(restored.PetSize == 150 && restored.PetTheme == "暮光紫" && restored.BubbleSeconds == 9 && !restored.PetAnimate, "外观与气泡设置序列化恢复");
            var old = JsonSerializer.Deserialize<Preferences>("{\"Pet\":true,\"Devices\":{}}")!;
            Require(old.PetSize == 100 && old.BubbleSeconds == 6, "旧版配置获得兼容默认值");
            Require(!old.AutoStart && !old.SilentStart && !old.ClickSoundEnabled && old.PetImagePath == "", "新增启动与素材设置默认关闭且兼容旧配置");
            var quiet = StartupPolicy.Resolve(new() { SilentStart = true, Pet = true }, false, false);
            Require(!quiet.ShowPanel && !quiet.ShowPet, "静默启动只驻留托盘，不显示面板或桌宠");
            var normal = StartupPolicy.Resolve(new() { Pet = true }, false, false);
            Require(normal.ShowPanel && normal.ShowPet, "普通启动显示面板并遵循桌宠偏好");
            Require(!StartupPolicy.Resolve(new(), true, false).ShowPet, "命令行静默启动覆盖显示偏好");
            var fakeStartup = new MemoryStartupStore();
            StartupRegistration.SetEnabled(true, fakeStartup);
            Require(StartupRegistration.IsEnabled(fakeStartup) && fakeStartup.Value!.EndsWith("\" --autostart"), "开机启动保存带引号的程序路径与自启动参数");
            StartupRegistration.SetEnabled(false, fakeStartup);
            Require(!StartupRegistration.IsEnabled(fakeStartup), "取消开机启动仅移除本程序项（内存存储验证）");
            Require(StartupRegistration.Command(@"C:\Program Files\电量桌宠\BatteryPet.exe") == "\"C:\\Program Files\\电量桌宠\\BatteryPet.exe\" --autostart", "启动命令正确处理中文和空格");

            string fixtures = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtures);
            string original = Path.Combine(fixtures, "original.png");
            using (var picture = new Bitmap(80, 40)) { using var g = Graphics.FromImage(picture); g.Clear(Color.Coral); picture.Save(original, ImageFormat.Png); }
            string imported = AssetStore.ImportImage(original, fixtures); File.Delete(original);
            using (var picture = PetImage.Load(imported))
                Require(picture.Size == new Size(80, 40), "导入图片复制保存，原文件删除后仍可读取");
            string transparent = Path.Combine(fixtures, "transparent.png"); using (var picture = new Bitmap(8, 8)) picture.Save(transparent, ImageFormat.Png);
            bool rejected = false; try { AssetStore.ImportImage(transparent, fixtures); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "完全透明的桌宠图片被拒绝");
            string bad = Path.Combine(fixtures, "bad.png"); File.WriteAllText(bad, "not an image");
            rejected = false; try { AssetStore.ImportImage(bad, fixtures); } catch { rejected = true; }
            Require(rejected, "损坏图片不覆盖现有角色");
            byte[] gifBytes = Convert.FromHexString("47494638396101000100800000FFFFFFE0404021FF0B4E45545343415045322E30030100000021F904040A0000002C000000000100010000020244010021F904040A0000002C00000000010001000002024C01003B");
            using (var gif = new PetImage(gifBytes))
            using (var image = new Bitmap(40, 40))
            using (var g = Graphics.FromImage(image))
            {
                gif.Draw(g, new(0, 0, 40, 40), 0, true); int first = image.GetPixel(20, 20).ToArgb();
                g.Clear(Color.Transparent); gif.Draw(g, new(0, 0, 40, 40), .12, true); int second = image.GetPixel(20, 20).ToArgb();
                g.Clear(Color.Transparent); gif.Draw(g, new(0, 0, 40, 40), .12, false); int paused = image.GetPixel(20, 20).ToArgb();
                Require(gif.Frames == 2 && first != second && paused == first, "GIF 按帧时长播放，关闭动画时显示首帧");
            }
            string wave = AssetStore.DefaultClick(fixtures); AssetStore.ValidateSound(File.ReadAllBytes(wave), ".wav");
            Require(File.Exists(AssetStore.ImportSound(wave, fixtures)), "默认点击音是可独立导入的 WAV 文件");
            rejected = false; try { AssetStore.ValidateSound(Encoding.UTF8.GetBytes("invalid"), ".mp3"); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "非法音频文件被拒绝");
            using (var audio = new ClickAudio())
            {
                Require(audio.Validate(wave) == null, "Windows 音频引擎可打开默认 WAV（不播放）");
                Require(audio.Validate(Path.Combine(fixtures, "missing.wav")) != null, "音频丢失返回错误，不影响桌宠运行");
            }

            using var sheet = new Bitmap(960, 360); using (var g = Graphics.FromImage(sheet))
            {
                g.Clear(Ui.Background);
                foreach (var item in SeaTheme.Names.Select((theme, i) => (theme, i)))
                {
                    PetRenderer.Draw(g, new(item.i * 320 + 40, 15, 240, 180), item.theme, 1, false, false, 0, PointF.Empty);
                    PetRenderer.Draw(g, new(item.i * 320 + 40, 180, 240, 180), item.theme, 1, true, false, .8, PointF.Empty);
                }
            }
            sheet.Save(Path.Combine(folder, "角色配色.png"), ImageFormat.Png);
            using (var bubble = new BatteryBubble(() => new("G502 X LIGHTSPEED", 86, true, false, false, "更新于 12:00:00", true)))
            using (var bitmap = bubble.RenderBitmap())
            {
                Require(bitmap.GetPixel(0, 0).A == 0 && bitmap.GetPixel(30, 30).A > 0, "气泡外侧透明，内部可见");
                bitmap.Save(Path.Combine(folder, "点击电量气泡.png"), ImageFormat.Png);
            }
            using var form = new DashboardForm(prefs, () => { }, () => { }, () => { }, _ => { }, (_, _) => { }, enabled => prefs.AutoStart = enabled);
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new(-10000, -10000);
            form.SetDevices(new() { new("test", "G502 X LIGHTSPEED", 86, true, false, DateTime.Today.AddHours(12), true, new()) }, "界面验证 · 示例数据", false, true);
            form.Show(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "设备面板.png"), ImageFormat.Png);
            }
            var controls = Descendants(form).ToList();
            ((Button)controls.Single(c => c is SeaButton && c.Text == "桌宠外观")).PerformClick(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "桌宠外观.png"), ImageFormat.Png);
            }
            var slider = controls.OfType<TrackBar>().Single(); slider.Value = 180;
            Require(prefs.PetSize == 180, "大小滑块写入真实设置");
            var combo = controls.OfType<ComboBox>().Single(c => c.Items.Contains("暮光紫")); combo.SelectedItem = "暮光紫";
            Require(prefs.PetTheme == "暮光紫", "配色选择写入真实设置");
            var refresh = controls.OfType<Button>().Single(c => c.Text == "刷新电量");
            Require(refresh.Bounds.Right <= refresh.Parent!.ClientSize.Width, "刷新按钮位于可见边界内");
            ((Button)controls.Single(c => c is SeaButton && c.Text == "启动与素材")).PerformClick(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "启动与素材.png"), ImageFormat.Png);
            }
            var resourcePage = controls.OfType<SettingsPage>().Single();
            resourcePage.AutoScrollPosition = new(0, resourcePage.VerticalScroll.Maximum); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "点击音效设置.png"), ImageFormat.Png);
            }
            resourcePage.AutoScrollPosition = Point.Empty;
            var quietSwitch = controls.OfType<CheckBox>().Single(c => c.Text == "静默启动：仅驻留托盘"); quietSwitch.Checked = true;
            Require(prefs.SilentStart, "静默开关写入真实偏好");
            var autoSwitch = controls.OfType<CheckBox>().Single(c => c.Text == "登录 Windows 时自动启动"); autoSwitch.Checked = true;
            Require(prefs.AutoStart, "开机启动控件调用注册回调，测试不更改系统启动项");
            var audioSwitch = controls.OfType<CheckBox>().Single(c => c.Text == "启用桌宠点击音效"); audioSwitch.Checked = true;
            Require(prefs.ClickSoundEnabled, "点击音效开关写入真实偏好");
            ((Button)controls.Single(c => c is SeaButton && c.Text == "设备适配")).PerformClick(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "设备适配.png"), ImageFormat.Png);
            }
            var adaptation = controls.OfType<AdaptationPage>().Single();
            Require(adaptation.Visible, "设备适配页可从主导航打开");
            Require(controls.OfType<Button>().Any(b => b.Text == "分析所选程序") && controls.OfType<Button>().Any(b => b.Text == "导出适配任务"), "适配页提供程序分析与 agent 任务导出入口");
            string sample = Path.Combine(AppContext.BaseDirectory, "Examples", "CustomPet", "batterypet-pet.png");
            Require(File.Exists(sample) && File.Exists(Path.Combine(Path.GetDirectoryName(sample)!, "SOURCE.md")), "随附示例角色图片及来源说明存在");
            prefs.PetImagePath = sample; prefs.PetSize = 100; form.SyncAppearance();
            ((Button)controls.Single(c => c is SeaButton && c.Text == "桌宠外观")).PerformClick(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "导入角色预览.png"), ImageFormat.Png);
            }
            form.Close();
            using (var pet = new PetWindow(new(), () => new("测试鼠标", 86, true, false, false, "测试", true), () => { }, () => { }, () => { }, () => { }, () => { }))
            {
                _ = pet.Handle; pet.Present();
                Require(pet.LastPresentSucceeded, "Windows 原生透明图层提交成功");
                using var image = pet.RenderBitmap();
                Require(image.GetPixel(0, 0).A == 0, "原生桌宠图层四角透明");
                Require(image.GetPixel(image.Width / 2, image.Height / 2).A > 0, "原生桌宠图层身体可见");
            }
            using (var pet = new PetWindow(new() { PetImagePath = Path.Combine(fixtures, "missing.png") }, () => new("测试", 86, true, false, false, "", true), () => { }, () => { }, () => { }, () => { }, () => { }))
            {
                Require(pet.ResourceWarning.Length > 0, "自定义图片丢失时回退内置角色");
            }
            using (var pet = new PetWindow(new() { PetImagePath = imported }, () => new("测试", 86, true, false, false, "", true), () => { }, () => { }, () => { }, () => { }, () => { }))
            {
                _ = pet.Handle; pet.Present(); using var image = pet.RenderBitmap();
                Require(pet.LastPresentSucceeded && image.GetPixel(image.Width / 2, image.Height / 2).R > 200 && image.GetPixel(0, 0).A == 0, "导入图片实际呈现在原生透明图层中");
            }
            File.WriteAllText(Path.Combine(folder, "结果.txt"), string.Join(Environment.NewLine, checks.Select(x => "PASS · " + x)));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(folder, "结果.txt"), string.Join(Environment.NewLine, checks) + "\nFAIL: " + e); }
    }
    sealed class MemoryStartupStore : IStartupStore
    {
        public string? Value;
        public string? Read() => Value;
        public void Write(string value) => Value = value;
        public void Remove() => Value = null;
    }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls) { yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
