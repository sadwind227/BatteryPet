namespace BatteryPet;

sealed class SettingsPage : FlowLayoutPanel
{
    readonly Preferences prefs;
    readonly Action apply;
    readonly Action<bool>? setAutoStart;
    readonly SeaSwitch autoStart = new("登录 Windows 时自动启动"), silent = new("静默启动：仅驻留托盘"), sound = new("启用桌宠点击音效");
    readonly Label imageLabel, soundLabel;
    readonly ClickAudio previewAudio = new();
    readonly List<RoundedPanel> cards = new();
    bool syncing;

    public SettingsPage(Preferences prefs, Action apply, Action<bool>? setAutoStart)
    {
        this.prefs = prefs; this.apply = apply; this.setAutoStart = setAutoStart;
        Dock = DockStyle.Fill; FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoScroll = true; Padding = new(0, 8, 0, 0);
        var start = Card("启动方式", 165);
        autoStart.Location = new(20, 43); autoStart.Width = 300; start.Controls.Add(autoStart);
        silent.Location = new(20, 80); silent.Width = 300; start.Controls.Add(silent);
        start.Controls.Add(Note("静默设置下次启动生效：仅保留托盘，可手动显示桌宠；低电量提醒仍按设置运行。", 20, 121));

        var picture = Card("自定义桌宠图片", 160);
        imageLabel = Note("内置小鲸鱼", 20, 43); picture.Controls.Add(imageLabel);
        var importImage = new SeaButton("导入图片…") { Location = new(20, 75) };
        var resetImage = new SeaButton("恢复内置角色") { Location = new(156, 75) };
        var sample = new SeaButton("使用随附角色") { Location = new(292, 75) };
        picture.Controls.Add(importImage); picture.Controls.Add(resetImage); picture.Controls.Add(sample);
        sample.Click += (_, _) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Examples", "CustomPet", "batterypet-pet.png");
            try
            {
                prefs.PetImagePath = AssetStore.ImportImage(path); prefs.PetImageName = "BatteryPet 鲸鱼女仆（示例）"; apply(); Sync();
            }
            catch (Exception e) { MessageBox.Show(FindForm(), "示例图片不可用：" + e.Message, "电量桌宠", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        picture.Controls.Add(Note("支持透明 PNG、JPG、GIF 动图、BMP；文件会复制保存，单个不超过 20 MB。", 20, 122));
        importImage.Click += (_, _) => ChooseImage();
        resetImage.Click += (_, _) => { prefs.PetImagePath = ""; prefs.PetImageName = ""; apply(); Sync(); };

        var audio = Card("自定义点击音效", 210);
        sound.Location = new(20, 42); sound.Width = 310; audio.Controls.Add(sound);
        soundLabel = Note("默认轻触音", 20, 82); audio.Controls.Add(soundLabel);
        var importSound = new SeaButton("导入音效…") { Location = new(20, 114) };
        var listen = new SeaButton("试听 / 停止") { Location = new(156, 114) };
        var resetSound = new SeaButton("恢复默认音效") { Location = new(292, 114) };
        audio.Controls.Add(importSound); audio.Controls.Add(listen); audio.Controls.Add(resetSound);
        audio.Controls.Add(Note("支持 WAV / MP3，单个不超过 20 MB；每次最多播放 10 秒。点击才响，拖动不响。", 20, 163));
        importSound.Click += (_, _) => ChooseSound();
        listen.Click += (_, _) =>
        {
            if (previewAudio.IsPlaying) { previewAudio.Stop(); soundLabel.Text = SoundName(); return; }
            string? error = previewAudio.Play(prefs.ClickSoundPath);
            soundLabel.Text = error ?? "正在试听，点击按钮可停止（最多 10 秒）";
        };
        resetSound.Click += (_, _) => { previewAudio.Stop(); prefs.ClickSoundPath = ""; prefs.ClickSoundName = ""; apply(); Sync(); };

        autoStart.CheckedChanged += (_, _) =>
        {
            if (syncing) return;
            setAutoStart?.Invoke(autoStart.Checked); Sync();
        };
        silent.CheckedChanged += (_, _) => { if (!syncing) { prefs.SilentStart = silent.Checked; apply(); } };
        sound.CheckedChanged += (_, _) => { if (!syncing) { prefs.ClickSoundEnabled = sound.Checked; if (!sound.Checked) previewAudio.Stop(); apply(); } };
        SizeChanged += (_, _) => ResizeCards();
        VisibleChanged += (_, _) => { if (!Visible) previewAudio.Stop(); };
        Sync();
    }
    RoundedPanel Card(string title, int height)
    {
        var card = new RoundedPanel { Height = height, Width = 740, Margin = new(0, 0, 0, 12), Padding = new(16) };
        card.Controls.Add(new Label { Text = title, Location = new(20, 15), Size = new(400, 25), Font = Ui.Font(11, FontStyle.Bold), ForeColor = Ui.Ink });
        cards.Add(card); Controls.Add(card); return card;
    }
    static Label Note(string text, int x, int y) => new() { Text = text, Location = new(x, y), AutoSize = false, Size = new(690, 30), Font = Ui.Font(8), ForeColor = Ui.Muted, AutoEllipsis = true };
    void ResizeCards()
    {
        foreach (var card in cards)
        {
            card.Width = Math.Max(400, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6);
            foreach (var label in card.Controls.OfType<Label>()) label.Width = Math.Max(100, card.Width - label.Left - (int)(20 * DeviceDpi / 96f));
        }
    }
    string SoundName() => string.IsNullOrEmpty(prefs.ClickSoundPath) ? "默认轻触音" : (File.Exists(prefs.ClickSoundPath) ? "已导入：" + prefs.ClickSoundName : "音效文件不可用，请重新导入");
    public void Sync()
    {
        syncing = true; autoStart.Checked = prefs.AutoStart; silent.Checked = prefs.SilentStart; sound.Checked = prefs.ClickSoundEnabled;
        imageLabel.Text = string.IsNullOrEmpty(prefs.PetImagePath) ? "内置小鲸鱼" : File.Exists(prefs.PetImagePath) ? "已导入：" + prefs.PetImageName : "图片文件不可用，桌宠已回退至内置角色";
        soundLabel.Text = SoundName(); syncing = false;
    }
    void ChooseImage()
    {
        using var picker = new OpenFileDialog { Title = "选择自定义桌宠图片", Filter = "桌宠图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            string path = AssetStore.ImportImage(picker.FileName);
            prefs.PetImagePath = path; prefs.PetImageName = Path.GetFileName(picker.FileName); apply(); Sync();
        }
        catch (Exception e) { MessageBox.Show(FindForm(), "图片导入失败：" + e.Message, "电量桌宠", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    void ChooseSound()
    {
        using var picker = new OpenFileDialog { Title = "选择点击音效", Filter = "音频文件|*.wav;*.mp3", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            previewAudio.Stop(); string path = AssetStore.ImportSound(picker.FileName);
            string? error = previewAudio.Validate(path); if (error != null) throw new InvalidDataException(error);
            prefs.ClickSoundPath = path; prefs.ClickSoundName = Path.GetFileName(picker.FileName); apply(); Sync();
        }
        catch (Exception e) { MessageBox.Show(FindForm(), "音效导入失败：" + e.Message, "电量桌宠", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    protected override void Dispose(bool disposing) { if (disposing) previewAudio.Dispose(); base.Dispose(disposing); }
}
