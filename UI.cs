using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;


namespace FlagInjector;

sealed class FlagRowVM : INotifyPropertyChanged
{
    string _value = "";
    public string Name { get; set; } = "";
    public string Value { get => _value; set { if (_value != value) { _value = value; OnProp(nameof(Value)); ValueEdited?.Invoke(this); } } }
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public string Mode { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int OrigIndex { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<FlagRowVM>? ValueEdited;
    void OnProp(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

sealed class MsgBox : Window
{
    bool _result;
    public MsgBox(string title, string message, bool yesNo = false)
    {
        Title = title; Width = 400; Height = 180; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Th.B(Th.C.Surface);
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Th.B(Th.C.Fg) });
        var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        if (yesNo)
        {
            var yes = new Button { Content = "Yes", Width = 80, Background = Th.B(Th.C.Accent), Foreground = Th.B(Th.C.Bg) };
            yes.Click += (_, _) => { _result = true; Close(); };
            var no = new Button { Content = "No", Width = 80, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg) };
            no.Click += (_, _) => Close();
            bp.Children.Add(yes); bp.Children.Add(no);
        }
        else
        {
            var ok = new Button { Content = "OK", Width = 80, Background = Th.B(Th.C.Accent), Foreground = Th.B(Th.C.Bg) };
            ok.Click += (_, _) => { _result = true; Close(); };
            bp.Children.Add(ok);
        }
        sp.Children.Add(bp); Content = sp;
    }
    public async Task<bool> Ask(Window owner) { await ShowDialog(owner); return _result; }
}

sealed class InputDlg : Window
{
    readonly TextBox _tb = new();
    string? _result;
    public InputDlg(string title, string prompt, string defaultVal = "")
    {
        Title = title; Width = 400; Height = 180; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Th.B(Th.C.Surface);
        var sp = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        sp.Children.Add(new TextBlock { Text = prompt, Foreground = Th.B(Th.C.Fg) });
        _tb.Text = defaultVal; _tb.Background = Th.B(Th.C.Bg); _tb.Foreground = Th.B(Th.C.Fg);
        sp.Children.Add(_tb);
        var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var ok = new Button { Content = "OK", Width = 80, Background = Th.B(Th.C.Accent), Foreground = Th.B(Th.C.Bg) };
        ok.Click += (_, _) => { _result = _tb.Text?.Trim(); Close(); };
        var cancel = new Button { Content = "Cancel", Width = 80, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg) };
        cancel.Click += (_, _) => Close();
        bp.Children.Add(ok); bp.Children.Add(cancel);
        sp.Children.Add(bp); Content = sp;
    }
    public async Task<string?> Ask(Window owner) { await ShowDialog(owner); return _result; }
}

sealed class HistoryWin : Window
{
    public HistoryWin(FlagEntry f)
    {
        Title = $"History \u2014 {f.Name}"; Width = 520; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Th.B(Th.C.Surface);
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserReorderColumns = false,
            Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg),
            HeadersVisibility = DataGridHeadersVisibility.Column
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Time", Binding = new Binding("Time"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Old Value", Binding = new Binding("Old"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "New Value", Binding = new Binding("New"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        var rows = f.History.AsEnumerable().Reverse().Select(h =>
        {
            var ts = DateTime.TryParse(h.Timestamp, out var dt) ? dt.ToLocalTime().ToString("g") : h.Timestamp;
            return new { Time = ts, Old = h.OldValue, New = h.NewValue };
        }).ToArray();
        grid.ItemsSource = rows.Length > 0 ? rows : new[] { new { Time = "", Old = "No history", New = "" } };
        Content = grid;
    }
}

sealed class DiffWin : Window
{
    public List<(string name, string value)> ToApply { get; } = new();
    readonly DataGrid _grid;
    public DiffWin(List<FlagEntry> current, Dictionary<string, string> imported)
    {
        Title = "Flag Comparison"; Width = 720; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Th.B(Th.C.Bg);
        var curMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in current) curMap[f.Name] = f.Value;
        var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in current) allKeys.Add(f.Name);
        foreach (var kv in imported) allKeys.Add(kv.Key);
        var rows = new List<object>();
        foreach (var k in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            bool inCur = curMap.TryGetValue(k, out var cv);
            bool inImp = imported.TryGetValue(k, out var iv);
            var st = inCur && inImp ? (cv == iv ? "Same" : "Modified") : inCur ? "Removed" : "New";
            rows.Add(new { Flag = k, Current = cv ?? "", Imported = iv ?? "", Status = st });
        }
        _grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true,
            Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg),
            SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column
        };
        _grid.Columns.Add(new DataGridTextColumn { Header = "Flag", Binding = new Binding("Flag"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Current", Binding = new Binding("Current"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Imported", Binding = new Binding("Imported"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _grid.ItemsSource = rows;
        var dock = new DockPanel { LastChildFill = true };
        var bp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 8, Margin = new Thickness(10) };
        DockPanel.SetDock(bp, Dock.Bottom);
        var btnApply = new Button { Content = "Apply Selected Differences", Width = 220, Background = Th.B(Th.C.Accent), Foreground = Th.B(Th.C.Bg) };
        btnApply.Click += (_, _) =>
        {
            foreach (var item in _grid.SelectedItems)
            {
                var type = item.GetType();
                var st = type.GetProperty("Status")?.GetValue(item)?.ToString() ?? "";
                if (st is "New" or "Modified")
                {
                    var n = type.GetProperty("Flag")?.GetValue(item)?.ToString() ?? "";
                    var v = type.GetProperty("Imported")?.GetValue(item)?.ToString() ?? "";
                    if (n != "") ToApply.Add((n, v));
                }
            }
            Close();
        };
        var btnClose = new Button { Content = "Close", Width = 80, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg) };
        btnClose.Click += (_, _) => Close();
        bp.Children.Add(btnApply); bp.Children.Add(btnClose);
        dock.Children.Add(bp); dock.Children.Add(_grid);
        Content = dock;
    }
}

sealed class PresetDlg : Window
{
    public string? ChosenLoad { get; private set; }
    public string? ChosenDelete { get; private set; }
    public string? SaveName { get; private set; }
    public PresetDlg(string[] presets, string lastPreset)
    {
        Title = "Presets"; Width = 360; Height = 420; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Th.B(Th.C.Surface);
        var sp = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = "Save Current", FontWeight = FontWeight.SemiBold, Foreground = Th.B(Th.C.Fg) });
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var saveTb = new TextBox { Width = 210, Text = lastPreset, Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg) };
        var saveBtn = new Button { Content = "Save", Width = 70, Background = Th.B(Th.C.Accent), Foreground = Th.B(Th.C.Bg) };
        saveBtn.Click += (_, _) => { SaveName = saveTb.Text?.Trim(); if (!string.IsNullOrEmpty(SaveName)) Close(); };
        saveRow.Children.Add(saveTb); saveRow.Children.Add(saveBtn);
        sp.Children.Add(saveRow);
        sp.Children.Add(new Border { Height = 1, Background = Th.B(Th.C.Border), Margin = new Thickness(0, 4) });
        sp.Children.Add(new TextBlock { Text = "Load Preset", FontWeight = FontWeight.SemiBold, Foreground = Th.B(Th.C.Fg) });
        var scroll = new ScrollViewer { MaxHeight = 220 };
        var list = new StackPanel { Spacing = 4 };
        foreach (var name in presets)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var cn = name;
            var loadBtn = new Button { Content = name, Width = 210, Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg), HorizontalContentAlignment = HorizontalAlignment.Left };
            loadBtn.Click += (_, _) => { ChosenLoad = cn; Close(); };
            var delBtn = new Button { Content = "\u00D7", Width = 30, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Red) };
            delBtn.Click += (_, _) => { ChosenDelete = cn; Close(); };
            row.Children.Add(loadBtn); row.Children.Add(delBtn);
            list.Children.Add(row);
        }
        if (presets.Length == 0) list.Children.Add(new TextBlock { Text = "(no presets)", Foreground = Th.B(Th.C.Sub) });
        scroll.Content = list; sp.Children.Add(scroll);
        var closeBtn = new Button { Content = "Close", Width = 80, HorizontalAlignment = HorizontalAlignment.Right, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg), Margin = new Thickness(0, 10, 0, 0) };
        closeBtn.Click += (_, _) => Close();
        sp.Children.Add(closeBtn);
        Content = sp;
    }
}

sealed class MainWindow : Window
{
    readonly AppLog _log;
    readonly AppSettings _settings;
    readonly CancellationTokenSource _cts = new();
    readonly MemEngine _mem = new();
    readonly OffsetStore _off = new();
    readonly FlogBank _bank;
    readonly UndoStack _undo = new();
    readonly PresetManager _presets;
    readonly List<FlagEntry> _flags = new();
    readonly string _dir, _savePath, _settingsPath;
    int _monLock, _busyLock, _wdLock, _saveVer;
    volatile bool _autoApply = true, _watchdog = true, _gameJoined;
    internal volatile bool _realExit;
    volatile int _lastPid; volatile bool _attaching;
    string _selAvailable = "";
    Timer? _saveDebounce;
    DispatcherTimer _monTimer = new(), _wdTimer = new(), _graceTimer = new(), _toastTimer = new();
    const int GraceIntervalMs = 1500, GraceMaxAttempts = 30, GraceStableNeeded = 6, BackupRotationCount = 5;
    int _graceAttempts, _graceStableCount;
    static readonly JsonSerializerOptions _jopt = new() { PropertyNameCaseInsensitive = true };
    static readonly string[] _processNames = { "RobloxPlayer", "Roblox" };
    ListBox _lbTop = new();
    DataGrid _dgBot = new();
    TextBox _searchTop = new(), _searchBot = new(), _edVal = new();
    TextBlock _lblTopHdr = new(), _lblBotHdr = new(), _lblSel = new(), _lblSt1 = new(), _lblSt2 = new(), _lblSt3 = new();
    Button _btnAdd = new();
    CheckBox _chkAuto = new(), _chkWd = new(), _chkTheme = new(), _chkConfirm = new();
    ComboBox _cmbTypeTop = new();
    string _cliImport = "", _cliPreset = "";
    bool _cliAutoApply, _cliMinimized;

    public MainWindow() : this(Array.Empty<string>()) { }

    public MainWindow(string[] args)
    {
        ParseArgs(args);
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "FlagInjectorCS");
        _savePath = Path.Combine(_dir, "flags.json");
        _settingsPath = Path.Combine(_dir, "settings.json");
        _off.Cache1 = Path.Combine(_dir, "offset_cache1.hpp");
        _off.Cache2 = Path.Combine(_dir, "offset_cache2.hpp");
        Directory.CreateDirectory(_dir);
        _log = new AppLog(_dir);
        _settings = AppSettings.Load(_settingsPath);
        _bank = new FlogBank(_mem);
        _presets = new PresetManager(_dir);
        _autoApply = _settings.AutoApply;
        _watchdog = _settings.Watchdog;
        Th.Set(_settings.DarkTheme);
        _saveDebounce = new Timer(SaveDebounceCallback, null, Timeout.Infinite, Timeout.Infinite);
        Title = "FFlag Injector (macOS)"; Width = _settings.W > 0 ? _settings.W : 820; Height = _settings.H > 0 ? _settings.H : 780;
        MinWidth = 640; MinHeight = 520; Background = Th.B(Th.C.Bg);
        if (_settings.X >= 0 && _settings.Y >= 0) { Position = new PixelPoint(_settings.X, _settings.Y); WindowStartupLocation = WindowStartupLocation.Manual; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _mem.Log += s => { _log.Info(s); Dispatcher.UIThread.Post(() => Toast(s)); };
        _off.Log += s => { _log.Info(s); Dispatcher.UIThread.Post(() => SetStatus(2, s)); };
        _bank.Log += s => { _log.Info(s); Dispatcher.UIThread.Post(() => SetStatus(3, s)); };
        Th.Changed += () => Dispatcher.UIThread.Post(ApplyTheme);
        LoadFlags();
        var ct = _cts.Token;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { _off.Fetch(ct); } catch (Exception ex) { _log.Error("OffsetFetch: " + ex.Message); }
            Dispatcher.UIThread.Post(() => { SetStatus(2, $"{_off.Count} offsets loaded"); RefreshTop(); RefreshBot(); TryInitBank(); });
        });
        _monTimer.Interval = TimeSpan.FromMilliseconds(1500); _monTimer.Tick += (_, _) => MonitorTick(); _monTimer.Start();
        _wdTimer.Interval = TimeSpan.FromMilliseconds(4000); _wdTimer.Tick += (_, _) => WatchdogTick(); _wdTimer.IsEnabled = _watchdog;
        _graceTimer.Interval = TimeSpan.FromMilliseconds(GraceIntervalMs); _graceTimer.Tick += (_, _) => GraceTick();
        _toastTimer.Interval = TimeSpan.FromMilliseconds(3500); _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); SetStatus(3, ""); };
        Closing += OnClosing;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);
        BuildUI();
        if (!string.IsNullOrEmpty(_cliImport)) Dispatcher.UIThread.Post(() => _ = ImportJson(_cliImport));
        if (!string.IsNullOrEmpty(_cliPreset)) Dispatcher.UIThread.Post(() => LoadPreset(_cliPreset));
        if (_cliAutoApply) _autoApply = true;
        if (_cliMinimized) WindowState = WindowState.Minimized;
    }

    void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
            switch (args[i].ToLowerInvariant())
            {
                case "--import" when i + 1 < args.Length: _cliImport = args[++i]; break;
                case "--auto-apply": _cliAutoApply = true; break;
                case "--minimized": _cliMinimized = true; break;
                case "--preset" when i + 1 < args.Length: _cliPreset = args[++i]; break;
            }
    }

    internal void PrepareExit() => _realExit = true;
    bool TrySetBusy() => Interlocked.CompareExchange(ref _busyLock, 1, 0) == 0;
    void ClearBusy() => Interlocked.Exchange(ref _busyLock, 0);

    void TryInitBank()
    {
        if (_bank.Ready || !_mem.On) return;
        if (_off.FlogPointer <= 0 && _off.StructOffsets.Count == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try {
                if (_off.StructOffsets.Count > 0) _bank.ApplyOffsets(_off.StructOffsets);
                if (_bank.Init()) Dispatcher.UIThread.Post(() => { Toast($"FlogBank: {_bank.Count} flags"); SetStatus(3, $"Bank: {_bank.Count} flags"); RefreshTop(); });
                else Dispatcher.UIThread.Post(() => SetStatus(3, "Bank: init failed"));
            } catch (Exception ex) { _log.Error("BankInit: " + ex.Message); Dispatcher.UIThread.Post(() => SetStatus(3, "Bank: error")); }
        });
    }

    void BuildUI()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(5, GridUnitType.Pixel)));
        root.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var topSection = new DockPanel();
        var topHdr = new StackPanel { Background = Th.B(Th.C.Bg), Spacing = 4, Margin = new Thickness(6, 4) };
        DockPanel.SetDock(topHdr, Dock.Top);
        _lblTopHdr = new TextBlock { Text = "AVAILABLE FLAGS", FontWeight = FontWeight.SemiBold, Foreground = Th.B(Th.C.Sub) };
        var topFilter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _searchTop = new TextBox { Watermark = "Search flags...", Width = 300, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg) };
        _searchTop.TextChanged += (_, _) => RefreshTop();
        _cmbTypeTop = new ComboBox { Width = 100, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg) };
        _cmbTypeTop.ItemsSource = new[] { "All Types", "Bool", "Int", "Float", "String" };
        _cmbTypeTop.SelectedIndex = 0; _cmbTypeTop.SelectionChanged += (_, _) => RefreshTop();
        topFilter.Children.Add(_searchTop); topFilter.Children.Add(_cmbTypeTop);
        topHdr.Children.Add(_lblTopHdr); topHdr.Children.Add(topFilter);

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Background = Th.B(Th.C.Surface), Spacing = 6, Height = 38 };
        DockPanel.SetDock(addRow, Dock.Bottom);
        _lblSel = new TextBlock { Text = "No flag selected", Foreground = Th.B(Th.C.Sub), VerticalAlignment = VerticalAlignment.Center, Width = 200, Margin = new Thickness(6, 0) };
        _edVal = new TextBox { Watermark = "Value", Width = 160, Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg) };
        _edVal.TextChanged += (_, _) => _btnAdd.IsEnabled = _selAvailable != "" && !string.IsNullOrWhiteSpace(_edVal.Text);
        _edVal.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddFlag(); e.Handled = true; } };
        _btnAdd = new Button { Content = "Add", Width = 60, IsEnabled = false, Background = Th.B(Th.C.Accent), Foreground = Th.B(Th.C.Bg) };
        _btnAdd.Click += (_, _) => AddFlag();
        addRow.Children.Add(_lblSel); addRow.Children.Add(_edVal); addRow.Children.Add(_btnAdd);

        _lbTop = new ListBox { Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg), SelectionMode = SelectionMode.Single };
        _lbTop.SelectionChanged += (_, _) => TopClick();
        _lbTop.DoubleTapped += (_, _) => { TopClick(); if (_selAvailable != "") _edVal.Focus(); };
        topSection.Children.Add(topHdr); topSection.Children.Add(addRow); topSection.Children.Add(_lbTop);
        Grid.SetRow(topSection, 0);

        var splitter = new GridSplitter { Height = 5, Background = Th.B(Th.C.Border), HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(splitter, 1);

        var botSection = new DockPanel();
        var botHdr = new StackPanel { Background = Th.B(Th.C.Bg), Spacing = 4, Margin = new Thickness(6, 4) };
        DockPanel.SetDock(botHdr, Dock.Top);
        _lblBotHdr = new TextBlock { Text = "MODIFIED FLAGS", FontWeight = FontWeight.SemiBold, Foreground = Th.B(Th.C.Sub) };
        _searchBot = new TextBox { Watermark = "Search modified flags...", Width = 300, Background = Th.B(Th.C.Surface), Foreground = Th.B(Th.C.Fg) };
        _searchBot.TextChanged += (_, _) => RefreshBot();
        var botFilter = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        botFilter.Children.Add(_searchBot);
        botHdr.Children.Add(_lblBotHdr); botHdr.Children.Add(botFilter);

        _dgBot = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = false, CanUserReorderColumns = true,
            CanUserResizeColumns = true, CanUserSortColumns = true,
            Background = Th.B(Th.C.Bg), Foreground = Th.B(Th.C.Fg),
            SelectionMode = DataGridSelectionMode.Extended, HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        _dgBot.Columns.Add(new DataGridTextColumn { Header = "Flag", Binding = new Binding("Name"), Width = new DataGridLength(3, DataGridLengthUnitType.Star), IsReadOnly = true });
        _dgBot.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding("Value") { Mode = BindingMode.TwoWay }, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _dgBot.Columns.Add(new DataGridTextColumn { Header = "Type", Binding = new Binding("Type"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        _dgBot.Columns.Add(new DataGridTextColumn { Header = "Status", Binding = new Binding("Status"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        _dgBot.Columns.Add(new DataGridTextColumn { Header = "Mode", Binding = new Binding("Mode"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });

        var ctx = new ContextMenu();
        void MI(string h, Action a) { var m = new MenuItem { Header = h }; m.Click += (_, _) => a(); ctx.Items.Add(m); }
        void Sep() { ctx.Items.Add(new Separator()); }
        MI("Apply Selected", () => ApplySelected());
        MI("Toggle On/Off", () => ToggleSelected());
        MI("Duplicate", () => DuplicateFlag());
        Sep();
        MI("Mode: OnJoin", () => SetModeSelected(ApplyMode.OnJoin));
        MI("Mode: Immediate", () => SetModeSelected(ApplyMode.Immediate));
        Sep();
        MI("Reset to Memory", () => ResetToDefault());
        MI("View History", () => ViewHistory());
        Sep();
        MI("Copy Name", () => CopySelectedNames());
        MI("Copy Value", () => CopySelectedValues());
        Sep();
        MI("Enable All", () => BulkEnable(true));
        MI("Disable All", () => BulkEnable(false));
        MI("Move Up", () => MoveSelected(-1));
        MI("Move Down", () => MoveSelected(1));
        Sep();
        MI("Remove (Del)", () => RemoveSelected());
        _dgBot.ContextMenu = ctx;
        botSection.Children.Add(botHdr); botSection.Children.Add(_dgBot);
        Grid.SetRow(botSection, 2);

        var actPanel = new WrapPanel { Background = Th.B(Th.C.Surface), Margin = new Thickness(0) };
        Button AB(string t, int w, bool p, Action c, bool d = false)
        {
            var b = new Button { Content = t, Width = w, Height = 30, Margin = new Thickness(3), Background = p ? Th.B(Th.C.Accent) : Th.B(Th.C.Surface), Foreground = p ? Th.B(Th.C.Bg) : d ? Th.B(Th.C.Red) : Th.B(Th.C.Fg) };
            b.Click += (_, _) => c(); return b;
        }
        actPanel.Children.Add(AB("\u25B6 Apply All (\u2318\u21E7A)", 200, true, () => _ = ApplyAllCmd()));
        actPanel.Children.Add(AB("Import (\u2318O)", 120, false, () => _ = ImportJson()));
        actPanel.Children.Add(AB("Export (\u2318S)", 120, false, () => _ = ExportJson()));
        actPanel.Children.Add(AB("ClientAppSettings", 140, false, ExportClientAppSettings));
        actPanel.Children.Add(AB("Copy JSON (\u2318\u21E7C)", 170, false, () => _ = CopyAllJson()));
        actPanel.Children.Add(AB("Compare...", 100, false, () => _ = ShowDiff()));
        actPanel.Children.Add(AB("Clear All", 90, false, () => _ = RemoveAll(), true));
        actPanel.Children.Add(AB("Presets", 90, false, () => _ = ShowPresetDlg()));
        _chkAuto = new CheckBox { Content = "Auto-apply", IsChecked = _autoApply, Foreground = Th.B(Th.C.Sub), Margin = new Thickness(8, 6, 0, 0) };
        _chkAuto.IsCheckedChanged += (_, _) => _autoApply = _chkAuto.IsChecked == true;
        _chkWd = new CheckBox { Content = "Watchdog", IsChecked = _watchdog, Foreground = Th.B(Th.C.Sub), Margin = new Thickness(4, 6, 0, 0) };
        _chkWd.IsCheckedChanged += (_, _) => { _watchdog = _chkWd.IsChecked == true; _wdTimer.IsEnabled = _watchdog; };
        _chkTheme = new CheckBox { Content = "Light", IsChecked = !Th.IsDark, Foreground = Th.B(Th.C.Sub), Margin = new Thickness(4, 6, 0, 0) };
        _chkTheme.IsCheckedChanged += (_, _) => Th.Toggle();
        _chkConfirm = new CheckBox { Content = "Confirm", IsChecked = _settings.ConfirmApplyAll, Foreground = Th.B(Th.C.Sub), Margin = new Thickness(4, 6, 0, 0) };
        _chkConfirm.IsCheckedChanged += (_, _) => _settings.ConfirmApplyAll = _chkConfirm.IsChecked == true;
        actPanel.Children.Add(_chkAuto); actPanel.Children.Add(_chkWd); actPanel.Children.Add(_chkTheme); actPanel.Children.Add(_chkConfirm);
        Grid.SetRow(actPanel, 3);

        var statusPanel = new StackPanel { Orientation = Orientation.Horizontal, Background = Th.B(Th.C.Surface), Spacing = 16, Height = 28 };
        _lblSt1 = new TextBlock { Text = "  Not detected", Foreground = Th.B(Th.C.Red), VerticalAlignment = VerticalAlignment.Center, Width = 280 };
        _lblSt2 = new TextBlock { Text = "  Offsets: loading...", Foreground = Th.B(Th.C.Sub), VerticalAlignment = VerticalAlignment.Center, Width = 220 };
        _lblSt3 = new TextBlock { Text = "", Foreground = Th.B(Th.C.Sub), VerticalAlignment = VerticalAlignment.Center };
        statusPanel.Children.Add(_lblSt1); statusPanel.Children.Add(_lblSt2); statusPanel.Children.Add(_lblSt3);
        Grid.SetRow(statusPanel, 4);

        root.Children.Add(topSection); root.Children.Add(splitter); root.Children.Add(botSection); root.Children.Add(actPanel); root.Children.Add(statusPanel);
        Content = root;
        RefreshTop(); RefreshBot();
    }

    void ApplyTheme()
    {
        Background = Th.B(Th.C.Bg);
        if (Application.Current is App app)
            app.RequestedThemeVariant = Th.IsDark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        RefreshTop(); RefreshBot();
    }

    void SetStatus(int idx, string txt)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SetStatus(idx, txt)); return; }
        switch (idx) { case 1: _lblSt1.Text = "  " + txt; break; case 2: _lblSt2.Text = "  " + txt; break; case 3: _lblSt3.Text = txt; break; }
    }

    void Toast(string msg, int ms = 3500) { SetStatus(3, msg); _toastTimer.Stop(); _toastTimer.Interval = TimeSpan.FromMilliseconds(ms); _toastTimer.Start(); }

    void MonitorTick()
    {
        if (Interlocked.CompareExchange(ref _monLock, 1, 0) != 0) return;
        if (_attaching) { Interlocked.Exchange(ref _monLock, 0); return; }
        Task.Run(() =>
        {
            int pid = 0;
            try
            {
                foreach (var pn in _processNames)
                {
                    foreach (var p in Process.GetProcessesByName(pn))
                    {
                        try { if (!p.HasExited) { pid = p.Id; break; } } catch { } finally { p.Dispose(); }
                    }
                    if (pid != 0) break;
                }
            }
            catch { }
            return pid;
        }).ContinueWith(t =>
        {
            try { int r = 0; try { r = t.Result; } catch { } Dispatcher.UIThread.Post(() => HandleMonResult(r)); }
            finally { Interlocked.Exchange(ref _monLock, 0); }
        });
    }

    void HandleMonResult(int pid)
    {
        if (pid != 0 && pid != _lastPid)
        {
            _lastPid = pid; _gameJoined = false; _graceTimer.Stop(); _bank.Reset(); _attaching = true;
            SetStatus(1, $"Attaching PID {pid}..."); _log.Info($"Detected PID {pid}");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try { ok = _mem.Attach(pid, "RobloxPlayer", _cts.Token); } catch (Exception ex) { _log.Error("Attach: " + ex.Message); } finally { _attaching = false; }
                Dispatcher.UIThread.Post(() =>
                {
                    if (ok) { SetStatus(1, $"PID {pid} \u2014 0x{_mem.Base:X}"); TryInitBank(); if (_autoApply && _off.Count > 0) StartGrace(); ApplyImmediateFlags(); }
                    else { SetStatus(1, "Attach failed"); _lastPid = 0; }
                });
            });
        }
        else if (pid == 0 && _lastPid != 0)
        {
            _graceTimer.Stop(); _gameJoined = false; _bank.Reset(); _mem.Detach(); _lastPid = 0;
            SetStatus(1, "Not detected"); Toast("Roblox disconnected"); _log.Info("Process exited");
        }
        else if (pid != 0 && !_attaching && _mem.On && !_mem.Alive())
        {
            _graceTimer.Stop(); _gameJoined = false; _bank.Reset(); _mem.Detach(); _lastPid = 0;
            SetStatus(1, "Process exited"); _log.Warn("Process exited unexpectedly");
        }
    }

    void ApplyImmediateFlags()
    {
        if (!_mem.On) return;
        var imm = _flags.Where(f => f.Enabled && f.Mode == ApplyMode.Immediate).ToArray();
        if (imm.Length == 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { int ok = 0; foreach (var f in imm) if (ApplySingle(f)) ok++;
            if (ok > 0) Dispatcher.UIThread.Post(() => { RefreshBot(); Toast($"Immediate: {ok} applied"); }); }
            catch (Exception ex) { _log.Error("Immediate: " + ex.Message); }
        });
    }

    void StartGrace()
    {
        _graceTimer.Stop(); _graceAttempts = 0; _graceStableCount = 0; _gameJoined = false;
        SetStatus(3, "Waiting for game join..."); _graceTimer.Start();
    }

    void GraceTick()
    {
        _graceAttempts++;
        if (!_mem.On || !_mem.Alive()) { _graceTimer.Stop(); _gameJoined = false; return; }
        bool ready = false;
        try { using var p = Process.GetProcessById(_mem.Pid); ready = !p.HasExited; } catch { }
        if (ready) _graceStableCount++; else _graceStableCount = 0;
        if (_graceStableCount >= GraceStableNeeded)
        {
            _graceTimer.Stop(); _gameJoined = true;
            if (_mem.On && _autoApply && (_off.Count > 0 || _bank.Ready)) { SetStatus(3, "Game joined, applying..."); ApplyAll(); }
            return;
        }
        if (_graceAttempts >= GraceMaxAttempts)
        {
            _graceTimer.Stop(); _gameJoined = true;
            if (_mem.On && _autoApply && (_off.Count > 0 || _bank.Ready)) { SetStatus(3, "Grace timeout, applying..."); ApplyAll(); }
            return;
        }
        SetStatus(3, $"Grace {_graceAttempts}/{GraceMaxAttempts}: {_graceStableCount}/{GraceStableNeeded}");
    }

    bool ResolveFlagAddr(FlagEntry f, out long addr, out string method)
    {
        addr = 0; method = "";
        var r = _off.Resolve(f.Name);
        if (r != null) { long o = _off.Offset(r); if (o > 0) { addr = _mem.Base + o; method = "offset"; return true; } }
        if (_bank.Ready) { var br = _bank.Resolve(f.Name); if (br != null) { long va = _bank.GetValueAddr(br); if (va > 0) { addr = va; method = "bank"; return true; } } }
        return false;
    }

    void WatchdogTick()
    {
        if (!_watchdog || !_gameJoined || !_mem.On) return;
        if (Interlocked.CompareExchange(ref _wdLock, 1, 0) != 0) return;
        if (_busyLock != 0) { Interlocked.Exchange(ref _wdLock, 0); return; }
        var snapshot = _flags.Where(f => f.Enabled).ToArray();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (_busyLock != 0 || !_gameJoined) return;
                int fix = 0;
                foreach (var f in snapshot)
                {
                    if (_busyLock != 0 || _cts.IsCancellationRequested) break;
                    if (!ResolveFlagAddr(f, out long addr, out string dummy)) continue;
                    var want = f.GetBytes(); var cur = _mem.ReadAbs(addr, want.Length);
                    if (cur != null && want.AsSpan().SequenceEqual(cur)) continue;
                    if (_mem.WriteFast(addr, want)) fix++;
                }
                    if (fix > 0) { _log.Info($"Watchdog re-applied {fix}"); Dispatcher.UIThread.Post(() => { Toast($"Watchdog re-applied {fix}"); RefreshBot(); }); }
            }
            catch (Exception ex) { _log.Error("Watchdog: " + ex.Message); }
            finally { Interlocked.Exchange(ref _wdLock, 0); }
        });
    }

    async Task ApplyAllCmd()
    {
        if (!_mem.On) { Toast("Not attached"); return; }
        if (_settings.ConfirmApplyAll)
        {
            var dlg = new MsgBox("Confirm", $"Apply {_flags.Count(f => f.Enabled)} enabled flags?", true);
            if (!await dlg.Ask(this)) return;
        }
        ApplyAll();
    }

    void ApplyAll()
    {
        if (!_mem.On) { Toast("Not attached"); return; }
        if (!TrySetBusy()) { Toast("Already in progress"); return; }
        var snapshot = _flags.Where(f => f.Enabled && (f.Mode == ApplyMode.OnJoin || _gameJoined)).ToArray();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                int ok = 0, fail = 0, skip = 0;
                foreach (var f in snapshot)
                {
                    if (_cts.IsCancellationRequested) break;
                    if (!ResolveFlagAddr(f, out long addr, out string method)) { f.Status = "No Offset"; skip++; continue; }
                    if (_mem.WriteFast(addr, f.GetBytes())) { f.Status = $"Applied ({method})"; ok++; } else { f.Status = "Failed"; fail++; }
                }
                _log.Info($"ApplyAll: ok={ok} fail={fail} skip={skip}");
                Dispatcher.UIThread.Post(() => { RefreshBot(); Toast($"Applied:{ok}  Failed:{fail}  Skip:{skip}", 4000); });
            }
            catch (Exception ex) { _log.Error("ApplyAll: " + ex.Message); Dispatcher.UIThread.Post(() => Toast("Apply error")); }
            finally { ClearBusy(); }
        });
    }

    bool ApplySingle(FlagEntry f)
    {
        if (!_mem.On) return false;
        if (!ResolveFlagAddr(f, out long addr, out string method)) { f.Status = "No Offset"; return false; }
        if (_mem.WriteAbs(addr, f.GetBytes())) { f.Status = $"Applied ({method})"; return true; }
        f.Status = "Failed"; return false;
    }

    int[] SelectedBotIndices()
    {
        var result = new List<int>();
        foreach (var item in _dgBot.SelectedItems) if (item is FlagRowVM vm && vm.OrigIndex >= 0 && vm.OrigIndex < _flags.Count) result.Add(vm.OrigIndex);
        return result.ToArray();
    }

    void RefreshTop()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingStripped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in _flags) { existing.Add(f.Name); existingStripped.Add(FlagPrefix.Strip(f.Name)); }
        var filter = _searchTop.Text ?? "";
        var typeFilter = _cmbTypeTop.SelectedIndex > 0 ? _cmbTypeTop.SelectedItem?.ToString() ?? "" : "";
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<string>();
        void TryAdd(string n)
        {
            if (existing.Contains(n)) return;
            var s = FlagPrefix.Strip(n);
            if (existingStripped.Contains(s)) return;
            if (!seen.Add(s)) return;
            if (filter.Length > 0 && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && s.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) return;
            if (typeFilter.Length > 0 && !FlagEntry.InferFromName(n).ToString().Equals(typeFilter, StringComparison.OrdinalIgnoreCase)) return;
            items.Add(n);
        }
        foreach (var n in _off.Names) TryAdd(n);
        if (_bank.Ready && filter.Length > 0) foreach (var n in _bank.Names) TryAdd(n);
        items.Sort(StringComparer.OrdinalIgnoreCase);
        _lbTop.ItemsSource = items;
        _lblTopHdr.Text = $"AVAILABLE FLAGS ({items.Count})";
        _selAvailable = ""; _lblSel.Text = "No flag selected"; _btnAdd.IsEnabled = false;
    }

    void RefreshBot()
    {
        var filter = _searchBot.Text ?? "";
        var rows = new List<FlagRowVM>();
        for (int i = 0; i < _flags.Count; i++)
        {
            var f = _flags[i];
            if (filter.Length > 0)
            {
                bool match = f.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || FlagPrefix.Strip(f.Name).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || f.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match) continue;
            }
            var st = f.Enabled ? (f.Status == "" ? "Active" : f.Status) : "Disabled";
            if (f.Enabled && st == "Active" && _off.Resolve(f.Name) == null && (!_bank.Ready || _bank.Resolve(f.Name) == null)) st = "No Offset";
            var vm = new FlagRowVM { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Status = st, Mode = f.Mode.ToString(), Enabled = f.Enabled, OrigIndex = i };
            vm.ValueEdited += OnValueEdited;
            rows.Add(vm);
        }
        _dgBot.ItemsSource = rows;
        _lblBotHdr.Text = $"MODIFIED FLAGS ({_flags.Count})";
    }

    void OnValueEdited(FlagRowVM vm)
    {
        if (vm.OrigIndex < 0 || vm.OrigIndex >= _flags.Count) return;
        var f = _flags[vm.OrigIndex];
        var nv = vm.Value?.Trim() ?? "";
        if (nv == f.Value || nv == "") return;
        _undo.Push(_flags);
        var old = f.Value; f.Value = nv; f.Type = FlagEntry.Infer(f.Name, nv); f.InvalidateCache();
        f.RecordChange(old, nv); DebounceSave();
        if (f.Enabled && _gameJoined && _mem.On)
            ThreadPool.QueueUserWorkItem(_ => { try { ApplySingle(f); Dispatcher.UIThread.Post(RefreshBot); } catch (Exception ex) { _log.Error("EditApply: " + ex.Message); } });
    }

    void TopClick()
    {
        var sel = _lbTop.SelectedItem as string;
        if (string.IsNullOrEmpty(sel)) { _selAvailable = ""; _lblSel.Text = "No flag selected"; _btnAdd.IsEnabled = false; return; }
        _selAvailable = sel; _lblSel.Text = sel; _btnAdd.IsEnabled = !string.IsNullOrWhiteSpace(_edVal.Text);
    }

    void AddFlag()
    {
        var v = _edVal.Text?.Trim() ?? "";
        if (_selAvailable == "" || v == "") return;
        if (_flags.Any(f => f.Name.Equals(_selAvailable, StringComparison.OrdinalIgnoreCase))) { Toast($"'{_selAvailable}' already exists"); return; }
        var t = FlagEntry.Infer(_selAvailable, v);
        if (t == FType.Int && !int.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { Toast("Invalid integer"); return; }
        if (t == FType.Float && !float.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _)) { Toast("Invalid float"); return; }
        _undo.Push(_flags);
        var fe = new FlagEntry { Name = _selAvailable, Value = v, Type = t, AddedAt = DateTime.UtcNow };
        _flags.Add(fe); DebounceSave(); RefreshTop(); RefreshBot(); _edVal.Text = "";
        if (_autoApply && _gameJoined && _mem.On) ThreadPool.QueueUserWorkItem(_ => { try { ApplySingle(fe); Dispatcher.UIThread.Post(RefreshBot); } catch (Exception ex) { _log.Error("AddApply: " + ex.Message); } });
        else if (_mem.On && fe.Mode == ApplyMode.Immediate) ThreadPool.QueueUserWorkItem(_ => { try { ApplySingle(fe); Dispatcher.UIThread.Post(RefreshBot); } catch (Exception ex) { _log.Error("AddApply: " + ex.Message); } });
        Toast($"Added: {fe.Name} = {v} [{t}]"); _log.Info($"Added: {fe.Name}={v}");
    }

    void ToggleSelected()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        _undo.Push(_flags);
        foreach (int i in sel) _flags[i].Enabled = !_flags[i].Enabled;
        DebounceSave(); RefreshBot();
    }

    void RemoveSelected()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        _undo.Push(_flags);
        foreach (int i in sel.OrderByDescending(x => x)) _flags.RemoveAt(i);
        DebounceSave(); RefreshTop(); RefreshBot(); Toast($"Removed {sel.Length} flag(s)");
    }

    async Task RemoveAll()
    {
        if (_flags.Count == 0) return;
        if (!await new MsgBox("Confirm", $"Remove all {_flags.Count} flags?", true).Ask(this)) return;
        _undo.Push(_flags); _flags.Clear(); DebounceSave(); RefreshTop(); RefreshBot(); Toast("All flags removed");
    }

    void BulkEnable(bool enable)
    {
        if (_flags.Count == 0) return;
        _undo.Push(_flags);
        foreach (var f in _flags) f.Enabled = enable;
        DebounceSave(); RefreshBot();
    }

    void ApplySelected()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0 || !_mem.On) return;
        var snapshot = sel.Select(i => _flags[i]).Where(f => f.Enabled).ToArray();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try { int ok = 0; foreach (var f in snapshot) if (ApplySingle(f)) ok++;
            Dispatcher.UIThread.Post(() => { RefreshBot(); Toast($"Applied {ok}/{snapshot.Length}"); }); }
            catch (Exception ex) { _log.Error("ApplySelected: " + ex.Message); }
        });
    }

    void DuplicateFlag()
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        var orig = _flags[sel[0]]; _undo.Push(_flags);
        var newName = orig.Name + "_copy";
        int n = 1; while (_flags.Any(f => f.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))) { n++; newName = orig.Name + $"_copy{n}"; }
        _flags.Add(new FlagEntry { Name = newName, Value = orig.Value, Type = orig.Type, Enabled = orig.Enabled, Mode = orig.Mode });
        DebounceSave(); RefreshBot(); Toast($"Duplicated: {newName}");
    }

    void SetModeSelected(ApplyMode mode)
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        _undo.Push(_flags);
        foreach (int i in sel) _flags[i].Mode = mode;
        DebounceSave(); RefreshBot();
    }

    void ResetToDefault()
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1 || !_mem.On) return;
        var f = _flags[sel[0]];
        if (!ResolveFlagAddr(f, out long addr, out string dummy)) { Toast("No offset"); return; }
        var cur = _mem.ReadAbs(addr, Math.Max(f.GetBytes().Length, 64));
        if (cur == null) { Toast("Cannot read memory"); return; }
        string memVal;
        switch (f.Type)
        {
            case FType.Bool: memVal = cur[0] != 0 ? "true" : "false"; break;
            case FType.Int: memVal = BitConverter.ToInt32(cur, 0).ToString(); break;
            case FType.Float: memVal = BitConverter.ToSingle(cur, 0).ToString(CultureInfo.InvariantCulture); break;
            default: int len = Array.IndexOf(cur, (byte)0); memVal = len >= 0 ? Encoding.UTF8.GetString(cur, 0, len) : Encoding.UTF8.GetString(cur); break;
        }
        _undo.Push(_flags);
        var old = f.Value; f.Value = memVal; f.InvalidateCache();
        f.RecordChange(old, memVal); DebounceSave(); RefreshBot(); Toast($"Reset {f.Name}: {memVal}");
    }

    void ViewHistory()
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        new HistoryWin(_flags[sel[0]]).ShowDialog(this);
    }

    async void CopySelectedNames()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null) await clip.SetTextAsync(string.Join(Environment.NewLine, sel.Select(i => _flags[i].Name)));
    }

    async void CopySelectedValues()
    {
        var sel = SelectedBotIndices(); if (sel.Length == 0) return;
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null) await clip.SetTextAsync(string.Join(Environment.NewLine, sel.Select(i => _flags[i].Value)));
    }

    void MoveSelected(int dir)
    {
        var sel = SelectedBotIndices(); if (sel.Length != 1) return;
        int idx = sel[0]; int ni = idx + dir;
        if (ni < 0 || ni >= _flags.Count) return;
        _undo.Push(_flags);
        (_flags[idx], _flags[ni]) = (_flags[ni], _flags[idx]);
        DebounceSave(); RefreshBot();
    }

    void PerformUndo() { var s = _undo.Undo(_flags); if (s == null) { Toast("Nothing to undo"); return; } RestoreSnapshot(s); DebounceSave(); RefreshTop(); RefreshBot(); Toast("Undo"); }
    void PerformRedo() { var s = _undo.Redo(_flags); if (s == null) { Toast("Nothing to redo"); return; } RestoreSnapshot(s); DebounceSave(); RefreshTop(); RefreshBot(); Toast("Redo"); }

    void RestoreSnapshot(UndoStack.FlagSnapshot[] snap)
    {
        _flags.Clear();
        foreach (var s in snap) _flags.Add(new FlagEntry { Name = s.Name, Value = s.Value, Type = s.Type, Enabled = s.Enabled, Mode = s.Mode, History = s.History });
    }

    async Task ShowDiff()
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider; if (sp == null) return;
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Select JSON to compare", FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } } });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath(); if (path == null) return;
        try
        {
            var imported = ParseFlatJson(File.ReadAllText(path, Encoding.UTF8));
            if (imported.Count == 0) { Toast("No flags found"); return; }
            var diff = new DiffWin(_flags, imported); await diff.ShowDialog(this);
            if (diff.ToApply.Count > 0)
            {
                _undo.Push(_flags); int added = 0, updated = 0;
                foreach (var (name, value) in diff.ToApply)
                {
                    var resolved = _off.Resolve(name) ?? (_bank.Ready ? _bank.Resolve(name) : null) ?? name;
                    int idx = _flags.FindIndex(f => f.Name.Equals(resolved, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) { var old = _flags[idx].Value; _flags[idx].Value = value; _flags[idx].Type = FlagEntry.Infer(resolved, value); _flags[idx].InvalidateCache(); _flags[idx].RecordChange(old, value); updated++; }
                    else { _flags.Add(new FlagEntry { Name = resolved, Value = value, Type = FlagEntry.Infer(resolved, value) }); added++; }
                }
                DebounceSave(); RefreshTop(); RefreshBot(); Toast($"Diff applied: +{added} ~{updated}");
            }
        }
        catch (Exception ex) { Toast("Diff error: " + ex.Message); }
    }

    Dictionary<string, string> ParseFlatJson(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    var val = p.Value.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => p.Value.GetRawText(), JsonValueKind.String => p.Value.GetString() ?? "", _ => "" };
                    if (val != "") result[p.Name] = val;
                }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind != JsonValueKind.Object) continue;
                    string n = "", v = "";
                    if (elem.TryGetProperty("n", out var np)) n = np.GetString() ?? ""; else if (elem.TryGetProperty("Name", out var np2)) n = np2.GetString() ?? "";
                    if (elem.TryGetProperty("v", out var vp)) v = vp.GetString() ?? ""; else if (elem.TryGetProperty("Value", out var vp2)) v = vp2.GetString() ?? "";
                    if (n != "" && v != "") result[n] = v;
                }
        }
        catch { }
        return result;
    }

    async Task ImportJson(string? path = null)
    {
        if (path == null)
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider; if (sp == null) return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import FFlags JSON", FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } } });
            if (files.Count == 0) return;
            path = files[0].TryGetLocalPath(); if (path == null) return;
        }
        try
        {
            var raw = File.ReadAllText(path, Encoding.UTF8);
            using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            _undo.Push(_flags);
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byStripped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _flags.Count; i++) { byName[_flags[i].Name] = i; byStripped[FlagPrefix.Strip(_flags[i].Name)] = i; }
            int added = 0, updated = 0;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var val = prop.Value.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => prop.Value.GetRawText(), JsonValueKind.String => prop.Value.GetString() ?? "", _ => "" };
                    if (val != "") ProcessImportEntry(prop.Name, val, true, byName, byStripped, ref added, ref updated);
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind != JsonValueKind.Object) continue;
                    string jn = "", val = ""; bool enabled = true; string ts = "", ms = "";
                    if (elem.TryGetProperty("n", out var nP)) jn = nP.GetString() ?? ""; else if (elem.TryGetProperty("Name", out var nP2)) jn = nP2.GetString() ?? "";
                    if (elem.TryGetProperty("v", out var vP)) val = vP.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => vP.GetRawText(), JsonValueKind.String => vP.GetString() ?? "", _ => "" }; else if (elem.TryGetProperty("Value", out var vP2)) val = vP2.ValueKind switch { JsonValueKind.True => "true", JsonValueKind.False => "false", JsonValueKind.Number => vP2.GetRawText(), JsonValueKind.String => vP2.GetString() ?? "", _ => "" };
                    if (elem.TryGetProperty("t", out var tP)) ts = tP.GetString() ?? "";
                    if (elem.TryGetProperty("m", out var mP)) ms = mP.GetString() ?? "";
                    if (elem.TryGetProperty("e", out var eP) && eP.ValueKind == JsonValueKind.False) enabled = false;
                    if (string.IsNullOrEmpty(jn)) continue;
                    var resolved = _off.Resolve(jn) ?? (_bank.Ready ? _bank.Resolve(jn) : null) ?? jn;
                    var t = Enum.TryParse<FType>(ts, true, out var parsed) ? parsed : FlagEntry.Infer(resolved, val);
                    Enum.TryParse<ApplyMode>(ms, true, out var mode);
                    var rs = FlagPrefix.Strip(resolved); int idx = -1;
                    if (byName.TryGetValue(resolved, out int i1)) idx = i1; else if (byStripped.TryGetValue(rs, out int i2)) idx = i2;
                    if (idx >= 0) { var old = _flags[idx].Value; _flags[idx].Value = val; _flags[idx].Type = t; _flags[idx].Enabled = enabled; _flags[idx].Mode = mode; _flags[idx].InvalidateCache(); _flags[idx].RecordChange(old, val); updated++; }
                    else { var fe = new FlagEntry { Name = resolved, Value = val, Type = t, Enabled = enabled, Mode = mode }; _flags.Add(fe); int ni = _flags.Count - 1; byName[resolved] = ni; byStripped[rs] = ni; added++; }
                }
            }
            if (added + updated == 0) { Toast("No matching flags found"); return; }
            DebounceSave(); RefreshTop(); RefreshBot();
            if (_autoApply && _gameJoined && _mem.On) ApplyAll();
            Toast($"Imported: +{added} ~{updated}"); _log.Info($"Import: +{added} ~{updated}");
        }
        catch (Exception ex) { Toast("Import error: " + ex.Message); _log.Error("Import: " + ex.Message); }
    }

    void ProcessImportEntry(string jn, string val, bool enabled, Dictionary<string, int> byName, Dictionary<string, int> byStripped, ref int added, ref int updated)
    {
        var resolved = _off.Resolve(jn) ?? (_bank.Ready ? _bank.Resolve(jn) : null) ?? jn;
        var t = FlagEntry.Infer(resolved, val);
        var rs = FlagPrefix.Strip(resolved); int idx = -1;
        if (byName.TryGetValue(resolved, out int i1)) idx = i1; else if (byStripped.TryGetValue(rs, out int i2)) idx = i2;
        if (idx >= 0) { var old = _flags[idx].Value; _flags[idx].Value = val; _flags[idx].Type = t; _flags[idx].Enabled = enabled; _flags[idx].InvalidateCache(); _flags[idx].RecordChange(old, val); updated++; }
        else { _flags.Add(new FlagEntry { Name = resolved, Value = val, Type = t }); int ni = _flags.Count - 1; byName[resolved] = ni; byStripped[rs] = ni; added++; }
    }

    async Task ExportJson()
    {
        if (_flags.Count == 0) { Toast("No flags"); return; }
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider; if (sp == null) return;
        var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Export FFlags JSON", DefaultExtension = "json", SuggestedFileName = "flags.json" });
        if (file == null) return;
        var path = file.TryGetLocalPath(); if (path == null) return;
        WriteExportJson(path); Toast($"Exported {_flags.Count} flags");
    }

    void ExportClientAppSettings()
    {
        if (_flags.Count == 0) { Toast("No flags"); return; }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Roblox", "ClientSettings");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ClientAppSettings.json");
        WriteExportJson(path); Toast($"Exported to {path}"); _log.Info($"CAS exported to {path}");
    }

    void WriteExportJson(string path)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var f in _flags.Where(f => f.Enabled))
                switch (f.Type)
                {
                    case FType.Bool: w.WriteBoolean(f.Name, f.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || f.Value == "1"); break;
                    case FType.Int: if (int.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv)) w.WriteNumber(f.Name, iv); else w.WriteString(f.Name, f.Value); break;
                    case FType.Float: if (float.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv)) w.WriteNumber(f.Name, fv); else w.WriteString(f.Name, f.Value); break;
                    default: w.WriteString(f.Name, f.Value); break;
                }
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    async Task CopyAllJson()
    {
        if (_flags.Count == 0) { Toast("No flags"); return; }
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var f in _flags.Where(f => f.Enabled))
                switch (f.Type)
                {
                    case FType.Bool: w.WriteBoolean(f.Name, f.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || f.Value == "1"); break;
                    case FType.Int: if (int.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out int iv)) w.WriteNumber(f.Name, iv); else w.WriteString(f.Name, f.Value); break;
                    case FType.Float: if (float.TryParse(f.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float fv)) w.WriteNumber(f.Name, fv); else w.WriteString(f.Name, f.Value); break;
                    default: w.WriteString(f.Name, f.Value); break;
                }
            w.WriteEndObject();
        }
        var clip = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clip != null) await clip.SetTextAsync(Encoding.UTF8.GetString(ms.ToArray()));
        Toast("Copied to clipboard");
    }

    async Task ShowPresetDlg()
    {
        var dlg = new PresetDlg(_presets.List(), _settings.LastPreset);
        await dlg.ShowDialog(this);
        if (!string.IsNullOrEmpty(dlg.SaveName))
        { _presets.Save(dlg.SaveName, _flags); _settings.LastPreset = dlg.SaveName; Toast($"Preset saved: {dlg.SaveName}"); }
        else if (!string.IsNullOrEmpty(dlg.ChosenLoad)) LoadPreset(dlg.ChosenLoad);
        else if (!string.IsNullOrEmpty(dlg.ChosenDelete)) { _presets.Delete(dlg.ChosenDelete); Toast($"Deleted: {dlg.ChosenDelete}"); }
    }

    void LoadPreset(string name)
    {
        var dtos = _presets.Load(name);
        if (dtos == null) { Toast("Preset not found"); return; }
        _undo.Push(_flags); _flags.Clear();
        foreach (var d in dtos)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            Enum.TryParse(d.Type, true, out FType t); Enum.TryParse(d.Mode, true, out ApplyMode m);
            _flags.Add(new FlagEntry { Name = d.Name, Value = d.Value, Type = t, Enabled = d.Enabled, Mode = m, History = d.History });
        }
        _settings.LastPreset = name; DebounceSave(); RefreshTop(); RefreshBot();
        if (_autoApply && _gameJoined && _mem.On) ApplyAll();
        Toast($"Loaded: {name} ({_flags.Count} flags)");
    }

    void DebounceSave() { _saveDebounce?.Change(500, Timeout.Infinite); }

    void SaveDebounceCallback(object? state)
    {
        int ver = Interlocked.Increment(ref _saveVer);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (Volatile.Read(ref _saveVer) != ver) return;
                var dtos = _flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History.Select(h => new FlagHistoryEntry { Timestamp = h.Timestamp, OldValue = h.OldValue, NewValue = h.NewValue }).ToList() }).ToArray();
                var data = JsonSerializer.Serialize(dtos);
                var sp = _savePath;
                Task.Run(() =>
                {
                    if (Volatile.Read(ref _saveVer) != ver) return;
                    try { RotateBackups(sp); var tmp = sp + ".tmp"; File.WriteAllText(tmp, data, new UTF8Encoding(false)); File.Move(tmp, sp, true); }
                    catch (Exception ex) { _log.Error("Save: " + ex.Message); }
                });
            }
            catch (Exception ex) { _log.Error("SaveSnapshot: " + ex.Message); }
        });
    }

    void FlushSave()
    {
                var dtos = _flags.Select(f => new FlagDto { Name = f.Name, Value = f.Value, Type = f.Type.ToString(), Enabled = f.Enabled, Mode = f.Mode.ToString(), History = f.History.Select(h => new FlagHistoryEntry { Timestamp = h.Timestamp, OldValue = h.OldValue, NewValue = h.NewValue }).ToList() }).ToArray();
        var data = JsonSerializer.Serialize(dtos);
        try { RotateBackups(_savePath); var tmp = _savePath + ".tmp"; File.WriteAllText(tmp, data, new UTF8Encoding(false)); File.Move(tmp, _savePath, true); } catch { }
    }

    void RotateBackups(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var dir = Path.GetDirectoryName(path)!; var name = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
            for (int i = BackupRotationCount - 1; i >= 1; i--)
            {
                var src = Path.Combine(dir, $"{name}.bak{i}{ext}"); var dst = Path.Combine(dir, $"{name}.bak{i + 1}{ext}");
                if (File.Exists(src)) { try { File.Delete(dst); } catch { } try { File.Move(src, dst); } catch { } }
            }
            try { File.Copy(path, Path.Combine(dir, $"{name}.bak1{ext}"), true); } catch { }
        }
        catch { }
    }

    void LoadFlags()
    {
        if (!File.Exists(_savePath)) return;
        try
        {
            var raw = File.ReadAllText(_savePath, Encoding.UTF8);
            var dtos = JsonSerializer.Deserialize<FlagDto[]>(raw, _jopt); if (dtos == null) return;
            foreach (var d in dtos)
            {
                if (string.IsNullOrWhiteSpace(d.Name)) continue;
                Enum.TryParse(d.Type, true, out FType t); Enum.TryParse(d.Mode, true, out ApplyMode m);
                _flags.Add(new FlagEntry { Name = d.Name, Value = d.Value, Type = t, Enabled = d.Enabled, Mode = m, History = d.History });
            }
            _log.Info($"Loaded {_flags.Count} flags");
        }
        catch (Exception ex) { _log.Error("Load: " + ex.Message); }
    }

    void OnClosing(object? s, WindowClosingEventArgs e)
    {
        if (!_realExit) { e.Cancel = true; WindowState = WindowState.Minimized; return; }
        _settings.AutoApply = _autoApply; _settings.Watchdog = _watchdog; _settings.DarkTheme = Th.IsDark;
        if (WindowState == WindowState.Normal) { _settings.X = Position.X; _settings.Y = Position.Y; _settings.W = (int)Width; _settings.H = (int)Height; }
        _cts.Cancel();
        _monTimer.Stop(); _wdTimer.Stop(); _graceTimer.Stop(); _toastTimer.Stop();
        _saveDebounce?.Dispose();
        _settings.Save(_settingsPath); FlushSave();
        _mem.Dispose(); _log.Dispose(); _cts.Dispose();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        bool cmd = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (cmd && shift && e.Key == Key.A) { _ = ApplyAllCmd(); e.Handled = true; }
        else if (cmd && e.Key == Key.O) { _ = ImportJson(); e.Handled = true; }
        else if (cmd && e.Key == Key.S) { _ = ExportJson(); e.Handled = true; }
        else if (cmd && e.Key == Key.Z) { PerformUndo(); e.Handled = true; }
        else if (cmd && e.Key == Key.Y) { PerformRedo(); e.Handled = true; }
        else if (cmd && shift && e.Key == Key.C) { _ = CopyAllJson(); e.Handled = true; }
        else if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
        else if (e.Key == Key.F5) { RefreshTop(); RefreshBot(); e.Handled = true; }
        else if (cmd && e.Key == Key.Q) { _realExit = true; Close(); e.Handled = true; }
    }

    void OnDragOver(object? s, DragEventArgs e) { if (e.Data.Contains(DataFormats.Files)) e.DragEffects = DragDropEffects.Copy; }
    void OnDrop(object? s, DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.ToArray();
        if (files != null && files.Length > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (path != null && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) _ = ImportJson(path);
        }
    }
}
