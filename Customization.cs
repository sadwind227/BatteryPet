using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace BatteryPet;

sealed record StartupDisplay(bool ShowPanel, bool ShowPet);
static class StartupPolicy
{
    public static StartupDisplay Resolve(Preferences prefs, bool forcedSilent, bool demo)
    {
        bool silent = !demo && (forcedSilent || prefs.SilentStart);
        return new(!silent, !silent && prefs.Pet);
    }
}

interface IStartupStore { string? Read(); void Write(string value); void Remove(); }
sealed class RegistryStartupStore : IStartupStore
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "BatteryPet.Desktop";
    public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(KeyPath); return key?.GetValue(Name) as string; }
    public void Write(string value) { using var key = Registry.CurrentUser.CreateSubKey(KeyPath); key.SetValue(Name, value, RegistryValueKind.String); }
    public void Remove() { using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true); key?.DeleteValue(Name, false); }
}
static class StartupRegistration
{
    public static string Executable => Path.Combine(AppContext.BaseDirectory, "BatteryPet.exe");
    public static string Command(string exe)
    {
        if (!Path.IsPathFullyQualified(exe) || exe.Contains('"')) throw new ArgumentException("启动程序路径无效");
        string command = '"' + exe + "\" --autostart";
        if (command.Length > 260) throw new ArgumentException("启动路径过长，请将程序放到较短的目录后设置开机启动");
        return command;
    }
    public static bool IsEnabled(IStartupStore? store = null) => string.Equals((store ?? new RegistryStartupStore()).Read(), Command(Executable), StringComparison.OrdinalIgnoreCase);
    public static void SetEnabled(bool enabled, IStartupStore? store = null)
    {
        store ??= new RegistryStartupStore();
        if (enabled) store.Write(Command(Executable)); else store.Remove();
    }
}

static class AssetStore
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryPet", "assets");
    public const int MaxBytes = 20 * 1024 * 1024;
    public static byte[] ReadBounded(string file)
    {
        using var input = File.OpenRead(file);
        if (input.Length is < 1 or > MaxBytes) throw new InvalidDataException("文件大小须在 1 字节至 20 MB 之间");
        var bytes = new byte[(int)input.Length]; input.ReadExactly(bytes); return bytes;
    }
    public static string ImportImage(string file, string? root = null)
    {
        var bytes = ReadBounded(file);
        using var check = new PetImage(bytes);
        string extension = check.FormatExtension;
        return Store(bytes, "images", extension, root);
    }
    public static string ImportSound(string file, string? root = null)
    {
        var bytes = ReadBounded(file); string ext = Path.GetExtension(file).ToLowerInvariant();
        ValidateSound(bytes, ext);
        return Store(bytes, "sounds", ext, root);
    }
    public static void ValidateSound(byte[] bytes, string extension)
    {
        bool wave = bytes.Length >= 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE";
        bool mp3 = bytes.Length >= 4 && (Encoding.ASCII.GetString(bytes, 0, 3) == "ID3" || bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0);
        if (extension == ".wav" && wave || extension == ".mp3" && mp3) return;
        throw new InvalidDataException("请选择有效的 WAV 或 MP3 音频文件");
    }
    static string Store(byte[] bytes, string category, string extension, string? root)
    {
        string folder = Path.Combine(root ?? Root, category); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension);
        if (!File.Exists(path))
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return path;
    }
    public static string DefaultClick(string? root = null)
    {
        string folder = Path.Combine(root ?? Root, "sounds"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "soft-click-v1.wav");
        if (File.Exists(path)) return path;
        const int rate = 22050, count = rate / 7;
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / rate, envelope = Math.Min(1, t / .005) * Math.Exp(-t * 32);
            writer.Write((short)(Math.Sin(2 * Math.PI * (730 * t + 90 * t * t)) * envelope * 4500));
        }
        File.WriteAllBytes(path, memory.ToArray()); return path;
    }
}

sealed class PetImage : IDisposable
{
    readonly MemoryStream stream;
    readonly Image image;
    readonly int[] delays;
    readonly int total;
    readonly int firstVisibleOffset;
    public string FormatExtension { get; }
    public int Frames => delays.Length;
    public Size Size => image.Size;
    public PetImage(byte[] bytes)
    {
        stream = new MemoryStream(bytes, false);
        try
        {
            image = Image.FromStream(stream, true, true);
            FormatExtension = image.RawFormat.Guid == ImageFormat.Png.Guid ? ".png" : image.RawFormat.Guid == ImageFormat.Gif.Guid ? ".gif" : image.RawFormat.Guid == ImageFormat.Jpeg.Guid ? ".jpg" : image.RawFormat.Guid == ImageFormat.Bmp.Guid ? ".bmp" : throw new InvalidDataException("图片支持 PNG、JPG、GIF、BMP");
            if (image.Width > 8192 || image.Height > 8192 || (long)image.Width * image.Height > 16_000_000) throw new InvalidDataException("图片尺寸过大，请缩小至 1600 万像素以内，单边不超过 8192 像素");
            int frames = image.FrameDimensionsList.Contains(FrameDimension.Time.Guid) ? image.GetFrameCount(FrameDimension.Time) : 1;
            if (frames > 300) throw new InvalidDataException("GIF 最多支持 300 帧");
            delays = Enumerable.Repeat(100, frames).ToArray();
            if (frames > 1)
            {
                try
                {
                    byte[] raw = image.GetPropertyItem(0x5100)?.Value ?? Array.Empty<byte>();
                    for (int i = 0; i < frames && i * 4 + 4 <= raw.Length; i++) delays[i] = (int)Math.Clamp((long)BitConverter.ToUInt32(raw, i * 4) * 10, 40, 2000);
                }
                catch (ArgumentException) { }
            }
            total = delays.Sum();
            using var visible = new Bitmap(32, 32); using var g = Graphics.FromImage(visible);
            bool any = false;
            for (int frame = 0; frame < Frames && !any; frame++)
            {
                if (Frames > 1) image.SelectActiveFrame(FrameDimension.Time, frame);
                g.Clear(Color.Transparent); g.DrawImage(image, 0, 0, 32, 32);
                for (int y = 0; y < 32 && !any; y++) for (int x = 0; x < 32; x++) if (visible.GetPixel(x, y).A > 8) { any = true; break; }
                if (any) firstVisibleOffset = delays.Take(frame).Sum();
            }
            if (!any) throw new InvalidDataException("图片完全透明，请选择有可见内容的桌宠图片");
        }
        catch { image?.Dispose(); stream.Dispose(); throw; }
    }
    public static PetImage Load(string path) => new(AssetStore.ReadBounded(path));
    public void Draw(Graphics g, Rectangle bounds, double seconds, bool animate, bool dragging = false, double poke = 0)
    {
        if (Frames > 1)
        {
            int t = animate ? (int)((seconds * 1000 + firstVisibleOffset) % total) : firstVisibleOffset, frame = 0;
            while (frame < Frames - 1 && t >= delays[frame]) { t -= delays[frame]; frame++; }
            image.SelectActiveFrame(FrameDimension.Time, frame);
        }
        var saved = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float margin = Math.Min(bounds.Width, bounds.Height) * .07f;
        float scale = Math.Min((bounds.Width - margin * 2) / image.Width, (bounds.Height - margin * 2) / image.Height);
        float w = image.Width * scale, h = image.Height * scale;
        float bob = animate ? (float)Math.Sin(seconds * 2.2) * bounds.Height * .012f : 0;
        float zoom = 1 + (float)poke * .045f;
        g.TranslateTransform(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f + bob);
        if (dragging) g.RotateTransform(-5);
        g.ScaleTransform(zoom, zoom);
        g.DrawImage(image, -w / 2, -h / 2, w, h); g.Restore(saved);
    }
    public void Dispose() { image.Dispose(); stream.Dispose(); }
}

sealed class ClickAudio : IDisposable
{
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint mciSendString(string command, StringBuilder? result, int length, IntPtr callback);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern bool mciGetErrorString(uint error, StringBuilder message, int length);
    readonly string alias = "bpet" + Guid.NewGuid().ToString("N");
    readonly System.Windows.Forms.Timer stopTimer = new() { Interval = 10000 };
    public bool IsPlaying
    {
        get { var result = new StringBuilder(32); return mciSendString($"status {alias} mode", result, result.Capacity, IntPtr.Zero) == 0 && result.ToString().Trim() == "playing"; }
    }
    public ClickAudio() { stopTimer.Tick += (_, _) => Stop(); }
    public string? Play(string path) => Open(path, true);
    public string? Validate(string path) => Open(path, false);
    string? Open(string path, bool play)
    {
        Stop();
        try
        {
            path = Path.GetFullPath(string.IsNullOrEmpty(path) ? AssetStore.DefaultClick() : path);
            if (!File.Exists(path)) return "找不到音效文件，请重新导入";
            if (path.Contains('"')) return "音效路径包含无效字符";
            uint error = mciSendString($"open \"{path}\" alias {alias}", null, 0, IntPtr.Zero);
            if (error == 0 && play) error = mciSendString($"play {alias} from 0", null, 0, IntPtr.Zero);
            if (error != 0)
            {
                var reason = new StringBuilder(256); mciGetErrorString(error, reason, reason.Capacity); Stop(); return "音效无法播放：" + reason;
            }
            if (play) stopTimer.Start(); else Stop();
            return null;
        }
        catch (Exception e) { Stop(); return "音效无法播放：" + e.Message; }
    }
    public void Stop() { stopTimer.Stop(); mciSendString($"close {alias}", null, 0, IntPtr.Zero); }
    public void Dispose() { Stop(); stopTimer.Dispose(); }
}
