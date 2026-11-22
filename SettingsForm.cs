namespace WinDock;

internal sealed class SettingsForm : Form
{
    private readonly DockSettings _settings;
    private readonly ComboBox _edge = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _font = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _fontSize = new() { Minimum = 8, Maximum = 28 };
    private readonly TrackBar _opacity = new() { Minimum = 40, Maximum = 255, TickFrequency = 20, Width = 220 };
    private readonly ComboBox _blur = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TrackBar _blurriness = new() { Minimum = 0, Maximum = 100, TickFrequency = 10, Width = 180 };
    private readonly Label _blurrinessValue = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly CheckBox _customize = new() { Text = "Customize blur", AutoSize = true };
    private readonly CheckBox _blurBehind = new() { Text = "Blur behind", AutoSize = true };
    private readonly CheckBox _acrylic = new() { Text = "Acrylic tint", AutoSize = true };
    private readonly CheckBox _floating = new() { Text = "Floating mode", AutoSize = true };
    private readonly NumericUpDown _gap = new() { Minimum = 0, Maximum = 64 };
    private readonly ComboBox _align = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TrackBar _spacing = new() { Minimum = 0, Maximum = 48, TickFrequency = 4, Width = 180 };
    private readonly Label _spacingValue = new() { AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ListBox _modules = new();
    private readonly ComboBox _date = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _time = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public SettingsForm(DockSettings settings)
    {
        _settings = Clone(settings);
        Text = "win-dock settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(460, 420);
        Font = new Font("Segoe UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(AppearancePage());
        tabs.TabPages.Add(ModulesPage());
        tabs.TabPages.Add(ClockPage());
        tabs.TabPages.Add(ThemesPage());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 42,
            Padding = new Padding(8),
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(tabs);
        Controls.Add(buttons);
        LoadValues();
        _floating.CheckedChanged += (_, _) => _gap.Enabled = _floating.Checked;
        _customize.CheckedChanged += (_, _) => UpdateBlurEditors();
        _blur.SelectedIndexChanged += (_, _) => UpdateBlurEditors();
    }

    public DockSettings Settings => _settings;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            ReadValues();
        }

        base.OnFormClosing(e);
    }

    private TabPage AppearancePage()
    {
        var page = new TabPage("Appearance");
        var layout = Stack(page);
        AddRow(layout, "Position", _edge);
        AddRow(layout, "Font", _font);
        AddRow(layout, "Font size", _fontSize);
        AddRow(layout, "Opacity", _opacity);
        AddRow(layout, "Blur", _blur);
        var blurRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        blurRow.Controls.Add(new Label { Text = "Blurriness", Width = 90, TextAlign = ContentAlignment.MiddleLeft });
        blurRow.Controls.Add(_blurriness);
        blurRow.Controls.Add(_blurrinessValue);
        layout.Controls.Add(blurRow);
        _blurriness.ValueChanged += (_, _) => _blurrinessValue.Text = _blurriness.Value.ToString();
        layout.Controls.Add(_customize);
        layout.Controls.Add(_blurBehind);
        layout.Controls.Add(_acrylic);
        layout.Controls.Add(_floating);
        AddRow(layout, "Gaps", _gap);
        return page;
    }

    private TabPage ModulesPage()
    {
        var page = new TabPage("Modules");
        var layout = Stack(page);
        AddRow(layout, "Alignment", _align);
        var spacingRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        spacingRow.Controls.Add(new Label { Text = "Spacing", Width = 90, TextAlign = ContentAlignment.MiddleLeft });
        spacingRow.Controls.Add(_spacing);
        spacingRow.Controls.Add(_spacingValue);
        layout.Controls.Add(spacingRow);
        _spacing.ValueChanged += (_, _) => _spacingValue.Text = _spacing.Value.ToString();
        _modules.Height = 180;
        _modules.Width = 280;
        layout.Controls.Add(_modules);
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(Button("On / off", ToggleModule));
        row.Controls.Add(Button("Up", () => MoveModule(-1)));
        row.Controls.Add(Button("Down", () => MoveModule(1)));
        layout.Controls.Add(row);
        return page;
    }

    private TabPage ClockPage()
    {
        var page = new TabPage("Date and time");
        var layout = Stack(page);
        AddRow(layout, "Date", _date);
        AddRow(layout, "Time", _time);
        return page;
    }

    private TabPage ThemesPage()
    {
        var page = new TabPage("Themes");
        var layout = Stack(page);
        AddRow(layout, "Theme", _theme);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Text = "System follows the Windows app theme. The other names are the palettes from the Windows 11 ISO builder.",
        });
        return page;
    }

    private void LoadValues()
    {
        Fill(_edge, Enum.GetNames<DockEdge>(), _settings.Edge.ToString());
        foreach (var family in new System.Drawing.Text.InstalledFontCollection().Families)
        {
            _font.Items.Add(family.Name);
        }

        var fontIndex = _font.FindStringExact(_settings.FontFamily);
        _font.SelectedIndex = fontIndex >= 0 ? fontIndex : _font.FindStringExact("Segoe UI");
        _fontSize.Value = _settings.FontSize;
        _opacity.Value = _settings.Opacity;
        Fill(_blur, ["None", "Glass", "Acrylic", "Mica"], _settings.Blur is "Blur" or "Glass" ? "Glass" : _settings.Blur);
        _blurriness.Value = Math.Clamp(_settings.BlurAmount, _blurriness.Minimum, _blurriness.Maximum);
        _blurrinessValue.Text = _blurriness.Value.ToString();
        _customize.Checked = _settings.CustomizeBlur;
        _blurBehind.Checked = _settings.BlurBehind;
        _acrylic.Checked = _settings.AcrylicTint;
        _floating.Checked = _settings.Floating;
        _gap.Value = _settings.Gap;
        _gap.Enabled = _settings.Floating;
        Fill(_align, Enum.GetNames<ModuleAlign>(), _settings.Align.ToString());
        _spacing.Value = Math.Clamp(_settings.Spacing, _spacing.Minimum, _spacing.Maximum);
        _spacingValue.Text = _spacing.Value.ToString();
        Fill(_date, ["Monday 23 Nov 2026", "23.11.2026", "Mon 23 Nov 2026", "Monday 23 November 2026"], DateLabel(_settings.Date));
        Fill(_time, ["24-hour  18:30", "12-hour  07:10 AM"], _settings.Time == TimeFormat.Hour12 ? "12-hour  07:10 AM" : "24-hour  18:30");
        Fill(_theme, BarThemes.Choices.Select(choice => choice.Label).ToArray(), ThemeLabel(_settings.Theme));
        RefreshModules();
        UpdateBlurEditors();
    }

    private void ReadValues()
    {
        _settings.Edge = Enum.Parse<DockEdge>(_edge.SelectedItem?.ToString() ?? "Top");
        _settings.FontFamily = _font.SelectedItem?.ToString() ?? "Segoe UI";
        _settings.FontSize = (int)_fontSize.Value;
        _settings.Opacity = _opacity.Value;
        _settings.Blur = _blur.SelectedItem?.ToString() switch
        {
            "Glass" => "Blur",
            var other => other ?? "None",
        };
        _settings.BlurAmount = _blurriness.Value;
        _settings.CustomizeBlur = _customize.Checked;
        _settings.BlurBehind = _blurBehind.Checked;
        _settings.AcrylicTint = _acrylic.Checked;
        _settings.Floating = _floating.Checked;
        _settings.Gap = (int)_gap.Value;
        _settings.Align = Enum.Parse<ModuleAlign>(_align.SelectedItem?.ToString() ?? "Left");
        _settings.Spacing = _spacing.Value;
        _settings.Date = _date.SelectedIndex switch
        {
            1 => DateFormat.Numeric,
            2 => DateFormat.ShortWeekday,
            3 => DateFormat.FullMonth,
            _ => DateFormat.WeekdayShortMonth,
        };
        _settings.Time = _time.SelectedIndex == 1 ? TimeFormat.Hour12 : TimeFormat.Hour24;
        var theme = BarThemes.Choices.FirstOrDefault(choice => choice.Label == _theme.SelectedItem?.ToString());
        _settings.Theme = theme.Id ?? "system";
        _settings.Normalize();
    }

    private void RefreshModules()
    {
        var selected = _modules.SelectedIndex;
        _modules.Items.Clear();
        foreach (var module in _settings.Modules)
        {
            var state = module.Enabled ? "On" : "Off";
            _modules.Items.Add($"{DockSettings.ModuleName(module.Id)}    {state}");
        }

        if (_modules.Items.Count > 0)
        {
            _modules.SelectedIndex = Math.Clamp(selected, 0, _modules.Items.Count - 1);
        }
    }

    private void ToggleModule()
    {
        var index = _modules.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _settings.Modules[index].Enabled = !_settings.Modules[index].Enabled;
        RefreshModules();
        _modules.SelectedIndex = index;
    }

    private void MoveModule(int direction)
    {
        var index = _modules.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _settings.Modules.Count)
        {
            return;
        }

        (_settings.Modules[index], _settings.Modules[target]) = (_settings.Modules[target], _settings.Modules[index]);
        RefreshModules();
        _modules.SelectedIndex = target;
    }

    private void UpdateBlurEditors()
    {
        var custom = _customize.Checked;
        var mode = _blur.SelectedItem?.ToString() ?? "None";
        _blurBehind.Enabled = custom;
        _acrylic.Enabled = custom;
        _opacity.Enabled = mode == "None" && !custom;
        _blurriness.Enabled = custom || mode is "Glass" or "Acrylic";
    }

    private static void Fill(ComboBox box, string[] items, string selected)
    {
        box.Items.Clear();
        box.Items.AddRange(items);
        var index = box.FindStringExact(selected);
        box.SelectedIndex = index >= 0 ? index : 0;
    }

    private static string DateLabel(DateFormat format) => format switch
    {
        DateFormat.Numeric => "23.11.2026",
        DateFormat.ShortWeekday => "Mon 23 Nov 2026",
        DateFormat.FullMonth => "Monday 23 November 2026",
        _ => "Monday 23 Nov 2026",
    };

    private static string ThemeLabel(string id)
        => BarThemes.Choices.FirstOrDefault(choice => choice.Id == id).Label ?? "System";

    private static DockSettings Clone(DockSettings source)
    {
        var copy = new DockSettings
        {
            Edge = source.Edge,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Opacity = source.Opacity,
            Blur = source.Blur,
            CustomizeBlur = source.CustomizeBlur,
            BlurBehind = source.BlurBehind,
            AcrylicTint = source.AcrylicTint,
            Floating = source.Floating,
            Gap = source.Gap,
            Align = source.Align,
            Spacing = source.Spacing,
            BlurAmount = source.BlurAmount,
            Date = source.Date,
            Time = source.Time,
            Theme = source.Theme,
            Modules = source.Modules.Select(module => new ModuleSetting { Id = module.Id, Enabled = module.Enabled }).ToList(),
        };
        copy.Normalize();
        return copy;
    }

    private static FlowLayoutPanel Stack(Control parent)
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12),
        };
        parent.Controls.Add(layout);
        return layout;
    }

    private static void AddRow(FlowLayoutPanel layout, string label, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label, Width = 90, TextAlign = ContentAlignment.MiddleLeft });
        control.Width = Math.Max(control.Width, 180);
        row.Controls.Add(control);
        layout.Controls.Add(row);
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true };
        button.Click += (_, _) => action();
        return button;
    }
}

internal sealed class DockOwner(IntPtr handle) : IWin32Window
{
    public IntPtr Handle { get; } = handle;
}
