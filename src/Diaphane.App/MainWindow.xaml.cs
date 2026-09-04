using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Diaphane.App.Browser;
using Diaphane.Data;
using Diaphane.Shell.Engine;
using Diaphane.Shell.Tabs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Colors = Microsoft.UI.Colors;

namespace Diaphane.App;

public sealed partial class MainWindow : Window
{
    private readonly CefHost _cef;
    private readonly ExtensionStore _extensions;
    public ShellViewModel Vm { get; }

    private CefSurface _page = null!;
    private CefSurface _dev = null!;

    public MainWindow()
    {
        InitializeComponent();
        Title = "diaphane";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "diaphane.ico");
        if (File.Exists(iconPath))
            AppWindow.SetIcon(iconPath);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diaphane");
        Directory.CreateDirectory(dataDir);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _extensions = new ExtensionStore(Path.Combine(dataDir, "extensions.db"));
        var privacySettings = new Diaphane.Privacy.PrivacySettingsStore(Path.Combine(dataDir, "privacy.json")).Load();
        _cef = new CefHost(DispatcherQueue, CefHost.ResolveNativeBinDir(),
            _extensions.EnabledPaths(), allowWidevine: privacySettings.EnableWidevine,
            enableDevTools: privacySettings.EnableDevTools);
        Vm = new ShellViewModel(_cef.Engine, dataDir, _extensions);
        Vm.PropertyChanged += OnVmPropertyChanged;
        Vm.Tabs.CollectionChanged += (_, _) => SyncTabStrip();
        ((Windows.Foundation.Collections.IObservableVector<object>)TabStrip.TabItems).VectorChanged += OnTabItemsReordered;
        Vm.SettingsChanged += ApplyTheme;
        Vm.Settings.EngineVersion = $"diaphane {Vm.Settings.Version}  ·  {_cef.Engine.Version}";
        ApplyTheme();
        RestoreWindowGeometry();

        _page = new CefSurface(BrowserImage, BrowserFocus);
        _dev = new CefSurface(DevToolsImage, DevToolsFocus);
        Address.GotFocus += (_, _) => { _page.Blur(); _dev.Blur(); };

        Root.Loaded += (_, _) =>
        {
            try
            {
                Vm.Start(0);
                SyncTabStrip();
                AttachActiveView();
                UpdateBookmarksPanel();
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                    $"{DateTime.Now:o} Loaded FAILED:\n{ex}\n\n");
            }

            if (Environment.GetEnvironmentVariable("DIAPHANE_SELFSHOT") is not null)
                _ = SelfCaptureLoopAsync();

            if (Environment.GetEnvironmentVariable("DIAPHANE_SMOKE") is { Length: > 0 } smokeDir)
                _ = SmokeHarnessAsync(smokeDir);
        };
        Closed += (_, _) =>
        {
            try { Vm.SaveSession(); } catch { /* best effort */ }
            try
            {
                Vm.SaveWindowState(AppWindow.Position.X, AppWindow.Position.Y,
                    AppWindow.Size.Width, AppWindow.Size.Height,
                    BookmarksColumn.Width.Value, DevToolsColumn.Width.Value);
            }
            catch { /* best effort */ }
            try { Vm.RunClearOnExitAsync().Wait(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
            Vm.Dispose();
            _cef.Dispose();
            _extensions.Dispose();
        };

        InstallAccelerators();
    }

    /// <summary>Restore window size/position from the last session; a never-saved X/Y (int.MinValue)
    /// leaves placement to the OS, matching a first run.</summary>
    private void RestoreWindowGeometry()
    {
        var (x, y, w, h) = Vm.WindowGeometry;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(w > 0 ? w : 1400, h > 0 ? h : 900));
        if (x != int.MinValue && y != int.MinValue)
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    // ---- x:Bind function helpers (sandbox accent) ----
    private static readonly SolidColorBrush s_sandboxText = new(Color.FromArgb(0xFF, 0xB3, 0x9D, 0xDB));

    public static Brush TabBrush(bool isSandbox) => isSandbox
        ? s_sandboxText
        : (Application.Current.Resources["TextFillColorPrimaryBrush"] as Brush ?? new SolidColorBrush(Colors.White));

    public static Visibility VisIf(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VisIfText(string? s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    public static bool Not(bool b) => !b;

    private void ApplyTheme()
    {
        Root.RequestedTheme = Vm.Theme switch
        {
            Diaphane.Shell.Settings.AppTheme.Light => ElementTheme.Light,
            Diaphane.Shell.Settings.AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    private void OnOpenUpdateDownload(object sender, RoutedEventArgs e)
    {
        if (Vm.Settings.UpdateDownloadUrl is { Length: > 0 } url)
        {
            Vm.NewTab();
            Vm.ActiveTab?.Navigate(url);
        }
    }

    public static Brush ToolbarBrush(bool isSandbox) => isSandbox
        ? new SolidColorBrush(Color.FromArgb(0x33, 0x67, 0x3A, 0xB7))
        : (Application.Current.Resources["LayerFillColorDefaultBrush"] as Brush ?? new SolidColorBrush(Colors.Transparent));

    // ---- keyboard accelerators ----
    private void InstallAccelerators()
    {
        void Add(VirtualKey key, VirtualKeyModifiers mods, Action run)
        {
            var a = new KeyboardAccelerator { Key = key, Modifiers = mods };
            a.Invoked += (_, e) => { e.Handled = true; run(); };
            Root.KeyboardAccelerators.Add(a);
        }

        const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;
        const VirtualKeyModifiers CtrlShift = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift;

        Add(VirtualKey.T, Ctrl, () => Vm.NewTab());
        Add(VirtualKey.W, Ctrl, () => Vm.CloseActiveTab());
        Add(VirtualKey.Tab, Ctrl, () => Vm.NextTab());
        Add(VirtualKey.Tab, CtrlShift, () => Vm.PrevTab());
        Add(VirtualKey.L, Ctrl, () => Address.Focus(FocusState.Programmatic));
        Add(VirtualKey.F5, VirtualKeyModifiers.None, () => Vm.ReloadCommand.Execute(null));
        Add(VirtualKey.Left, VirtualKeyModifiers.Menu, () => Vm.GoBackCommand.Execute(null));
        Add(VirtualKey.Right, VirtualKeyModifiers.Menu, () => Vm.GoForwardCommand.Execute(null));
        Add(VirtualKey.D, Ctrl, () => Vm.ToggleBookmarkCommand.Execute(null));
        Add(VirtualKey.B, CtrlShift, () => Vm.ToggleBookmarksBarCommand.Execute(null));
        Add(VirtualKey.N, CtrlShift, () => Vm.NewSandboxTabCommand.Execute(null));
        Add(VirtualKey.Delete, CtrlShift, () => Vm.TogglePrivacyPanelCommand.Execute(null));
        Add(VirtualKey.E, CtrlShift, () => Vm.ToggleExtensionsPanelCommand.Execute(null));
        Add(VirtualKey.I, CtrlShift, () => Vm.ToggleDevToolsCommand.Execute(null));
        Add(VirtualKey.F12, VirtualKeyModifiers.None, () => Vm.ToggleDevToolsCommand.Execute(null));
        Add(VirtualKey.M, CtrlShift, () => Vm.ToggleMediaPanelCommand.Execute(null));
    }

    // ---- diaphane://extensions ----
    private async void OnLoadUnpackedClick(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,
            WinRT.Interop.WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            Vm.Extensions.LoadUnpacked(folder.Path);
    }

    private void OnRemoveExtensionClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is Browser.ExtensionRow row)
            Vm.Extensions.RemoveCommand.Execute(row);
    }

    // Diagnostic: RenderTargetBitmap captures the live XAML visual tree (incl. the
    // CEF Image) straight from the compositor — the only reliable way to prove
    // what actually rendered, since BitBlt/PrintWindow can't see WinUI DComp content.
    private async System.Threading.Tasks.Task SelfCaptureLoopAsync()
    {
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
        for (int i = 0; i < 8; i++)
        {
            await System.Threading.Tasks.Task.Delay(2500);
            try
            {
                var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
                await rtb.RenderAsync(Root);
                var px = (await rtb.GetPixelsAsync()).ToArray();
                long nonBlack = 0;
                for (int k = 0; k < px.Length; k += 4)
                    if (px[k] > 8 || px[k + 1] > 8 || px[k + 2] > 8) nonBlack++;
                var file = await folder.CreateFileAsync($"diaphane-shot{i}.png",
                    Windows.Storage.CreationCollisionOption.ReplaceExisting);
                using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                var enc = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
                enc.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                    (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, px);
                await enc.FlushAsync();
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                    $"{DateTime.Now:o} selfshot {i}: {rtb.PixelWidth}x{rtb.PixelHeight} nonBlackPx={nonBlack}\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "diaphane-app.log"),
                    $"{DateTime.Now:o} selfshot {i}: {ex.Message}\n");
            }
        }
    }

    // Support harness for scripts/ui-smoke.ps1: navigate to a fixed form page,
    // announce readiness, then poll the DOM into a state file the script asserts
    // on after driving real OS mouse/keyboard input at the page.
    private async System.Threading.Tasks.Task SmokeHarnessAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        var page = "data:text/html," + Uri.EscapeDataString(
            "<!doctype html><meta charset=utf-8><body style='margin:0;font:28px sans-serif'>" +
            "<input id=txt style='position:absolute;left:0;top:0;width:640px;height:90px' " +
            "onfocus=\"this.dataset.f=1\" onblur=\"this.dataset.f=0\">" +
            "<input id=chk type=checkbox style='position:absolute;left:0;top:120px;width:60px;height:60px'>" +
            "<textarea id=ta style='position:absolute;left:0;top:200px;width:640px;height:120px'></textarea>");

        await System.Threading.Tasks.Task.Delay(2500);
        Vm.ActiveTab?.Navigate(page);
        await System.Threading.Tasks.Task.Delay(4000);
        File.WriteAllText(Path.Combine(dir, "ready"), DateTime.Now.ToString("o"));

        for (int i = 0; i < 40; i++)
        {
            try
            {
                var json = await Vm.ActiveTab!.EvaluateJavaScriptAsync(
                    "JSON.stringify({txt:txt.value, txtFocused:txt.dataset.f==='1', " +
                    "active:document.activeElement.id, chk:chk.checked, ta:ta.value})");
                using var d = System.Text.Json.JsonDocument.Parse(json);
                File.WriteAllText(Path.Combine(dir, "state.json"),
                    d.RootElement.GetProperty("result").GetProperty("value").GetString() ?? "{}");
            }
            catch { /* keep polling */ }
            await System.Threading.Tasks.Task.Delay(500);
        }
    }

    private void OnVmPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.ActiveTab):
                SelectActiveInStrip();
                AttachActiveView();
                break;
            case nameof(ShellViewModel.IsLoading):
                LoadBar.Visibility = Vm.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                break;
            case nameof(ShellViewModel.ShowDevTools):
                UpdateDevToolsPane();
                break;
            case nameof(ShellViewModel.ShowBookmarksBar):
                UpdateBookmarksPanel();
                break;
        }
    }

    // ---- surface wiring ----
    private void AttachActiveView()
    {
        _page.Attach(Vm.ActiveTab?.Offscreen);
        // DevTools belongs to a specific tab — drop it when switching tabs.
        if (Vm.ShowDevTools) Vm.ShowDevTools = false;
    }

    private async void UpdateDevToolsPane()
    {
        if (Vm.ShowDevTools && Vm.ActiveTab is { } tab)
        {
            if (DevToolsColumn.Width.Value < 1)
                DevToolsColumn.Width = new GridLength(
                    Vm.SavedDevToolsPanelWidth >= 1 ? Vm.SavedDevToolsPanelWidth : Math.Max(360, Root.ActualWidth * 0.42));
            DevToolsSplitter.Visibility = Visibility.Visible;
            Root.UpdateLayout();   // give the pane a real size before we ask CEF to render into it

            var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
            var dt = await tab.OpenDevToolsAsync(
                (int)Math.Round(DevToolsRegion.ActualWidth * scale),
                (int)Math.Round(DevToolsRegion.ActualHeight * scale));

            if (dt is null || !Vm.ShowDevTools)   // resolve failed or user closed while we waited
            {
                Vm.ShowDevTools = false;
                return;
            }
            _dev.Attach(dt);
        }
        else
        {
            _dev.Attach(null);
            Vm.ActiveTab?.CloseDevTools();
            DevToolsSplitter.Visibility = Visibility.Collapsed;
            DevToolsColumn.Width = new GridLength(0);
        }
    }

    // ---- history flyout ----
    private void OnHistoryFlyoutOpening(object? sender, object e)
        => HistoryList.ItemsSource = Vm.RecentHistory();

    private void OnHistoryItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Diaphane.Data.VisitEntry v)
            Vm.NavigateCommand.Execute(v.Url);
    }

    // ---- bookmarks panel ----
    private void UpdateBookmarksPanel()
    {
        if (Vm.ShowBookmarksBar)
        {
            if (BookmarksColumn.Width.Value < 1)
                BookmarksColumn.Width = new GridLength(Vm.SavedBookmarksPanelWidth >= 1 ? Vm.SavedBookmarksPanelWidth : 260);
        }
        else
        {
            BookmarksColumn.Width = new GridLength(0);
        }
    }

    private void BookmarksTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is BookmarkNode { IsFolder: false } node)
            Vm.OpenBookmarkCommand.Execute(node.Model);
    }

    private void BookmarksTree_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var node = (e.OriginalSource as FrameworkElement)?.DataContext as BookmarkNode
                   ?? FindBookmarkNodeInParents(e.OriginalSource as DependencyObject);

        var element = e.OriginalSource as FrameworkElement ?? BookmarksTree;
        var flyout = new MenuFlyout();

        if (node is null)
        {
            var createGroup = new MenuFlyoutItem { Text = "Create Group…" };
            createGroup.Click += async (_, _) => await PromptCreateGroupAsync(null);
            flyout.Items.Add(createGroup);
        }
        else if (node.IsFolder)
        {
            var createGroup = new MenuFlyoutItem { Text = "Create Group…" };
            createGroup.Click += async (_, _) => await PromptCreateGroupAsync(node.Model.Id);
            flyout.Items.Add(createGroup);

            var rename = new MenuFlyoutItem { Text = "Rename…" };
            rename.Click += async (_, _) => await PromptRenameAsync(node.Model);
            flyout.Items.Add(rename);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var openAll = new MenuFlyoutItem { Text = "Open All in new tabs" };
            openAll.Click += (_, _) => Vm.OpenGroupInNewTabs(node, sandbox: false);
            flyout.Items.Add(openAll);

            var openAllSandbox = new MenuFlyoutItem { Text = "Open all in new Sandbox tabs" };
            openAllSandbox.Click += (_, _) => Vm.OpenGroupInNewTabs(node, sandbox: true);
            flyout.Items.Add(openAllSandbox);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var remove = new MenuFlyoutItem { Text = "Remove" };
            remove.Click += (_, _) => Vm.RemoveBookmarkCommand.Execute(node.Model);
            flyout.Items.Add(remove);
        }
        else
        {
            var open = new MenuFlyoutItem { Text = "Open" };
            open.Click += (_, _) => Vm.OpenBookmarkCommand.Execute(node.Model);
            flyout.Items.Add(open);

            var openNewTab = new MenuFlyoutItem { Text = "Open in new tab" };
            openNewTab.Click += (_, _) => Vm.OpenBookmarkInNewTabCommand.Execute(node.Model);
            flyout.Items.Add(openNewTab);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var edit = new MenuFlyoutItem { Text = "Edit…" };
            edit.Click += async (_, _) => await PromptEditBookmarkAsync(node.Model);
            flyout.Items.Add(edit);

            var remove = new MenuFlyoutItem { Text = "Remove" };
            remove.Click += (_, _) => Vm.RemoveBookmarkCommand.Execute(node.Model);
            flyout.Items.Add(remove);
        }

        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private static BookmarkNode? FindBookmarkNodeInParents(DependencyObject? start)
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d is FrameworkElement { DataContext: BookmarkNode node }) return node;
        return null;
    }

    private async Task PromptCreateGroupAsync(long? parentId)
    {
        var name = await PromptTextAsync("Create Group", "Group name", "New Group", "Create");
        if (name is not null) Vm.CreateGroup(parentId, name);
    }

    private async Task PromptRenameAsync(Bookmark b)
    {
        var name = await PromptTextAsync("Rename", "Name", b.Title, "Rename");
        if (name is not null && name != b.Title) Vm.RenameBookmark(b, name);
    }

    private async Task<string?> PromptTextAsync(string title, string header, string prefill, string primary)
    {
        var box = new TextBox { Header = header, Text = prefill, Width = 300 };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = title,
            Content = box,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text.Trim()
            : null;
    }

    private async Task PromptEditBookmarkAsync(Bookmark b)
    {
        var titleBox = new TextBox { Header = "Name", Text = b.Title, Width = 340 };
        var urlBox = new TextBox { Header = "URL", Text = b.Url ?? "", Width = 340, Margin = new Thickness(0, 8, 0, 0) };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Edit bookmark",
            Content = new StackPanel { Children = { titleBox, urlBox } },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var title = titleBox.Text.Trim();
        var url = urlBox.Text.Trim();
        if (title.Length == 0 || url.Length == 0) return;
        Vm.EditBookmark(b, title, url);
    }

    // ---- bookmarks drag-and-drop (per-row CanDrag, mirrors the tab-strip's approach) ----
    private BookmarkNode? _draggedBookmark;

    private void BookmarkRow_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is BookmarkNode node)
        {
            _draggedBookmark = node;
            args.AllowedOperations = DataPackageOperation.Move;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText(node.DisplayName);
        }
        else
        {
            args.Cancel = true;
        }
    }

    private void BookmarkRow_DropCompleted(UIElement sender, DropCompletedEventArgs args) => _draggedBookmark = null;

    private void BookmarkRow_DragOver(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BookmarkNode target || _draggedBookmark is not { } dragged
            || !target.IsFolder || target.Model.Id == dragged.Model.Id)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = $"Move into {target.DisplayName}";
        e.Handled = true;
    }

    private void BookmarkRow_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BookmarkNode target || _draggedBookmark is not { } dragged)
            return;
        e.Handled = true;
        Vm.MoveBookmark(dragged.Model.Id, target.Model.Id);
        _draggedBookmark = null;
    }

    // Dropping on the panel's empty background (not on any row) moves the bookmark back to the top level.
    private void BookmarksRoot_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _draggedBookmark is not null ? DataPackageOperation.Move : DataPackageOperation.None;
        if (_draggedBookmark is null) return;
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = false;
        e.DragUIOverride.Caption = "Move to top level";
    }

    private void BookmarksRoot_Drop(object sender, DragEventArgs e)
    {
        if (_draggedBookmark is { } dragged) Vm.MoveBookmark(dragged.Model.Id, null);
        _draggedBookmark = null;
    }

    // ---- bookmarks panel splitter ----
    private bool _draggingBookmarksSplitter;
    private void OnBookmarksSplitterPressed(object sender, PointerRoutedEventArgs e)
    {
        _draggingBookmarksSplitter = true;
        BookmarksSplitter.CapturePointer(e.Pointer);
    }
    private void OnBookmarksSplitterMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingBookmarksSplitter) return;
        var x = e.GetCurrentPoint(Root).Position.X;
        var w = Math.Clamp(x - 48, 200, 460);
        BookmarksColumn.Width = new GridLength(w);
    }
    private void OnBookmarksSplitterReleased(object sender, PointerRoutedEventArgs e)
    {
        _draggingBookmarksSplitter = false;
        BookmarksSplitter.ReleasePointerCapture(e.Pointer);
    }

    // ---- page surface (delegates to CefSurface) ----
    private void OnBrowserRegionChanged(object sender, SizeChangedEventArgs e) => _page.Resize();
    private void OnPointerMoved(object sender, PointerRoutedEventArgs e) => _page.PointerMoved(e);
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => _page.PointerExited(e);
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e) => _page.PointerPressed(e);
    private void OnPointerReleased(object sender, PointerRoutedEventArgs e) => _page.PointerReleased(e);
    private void OnPointerWheel(object sender, PointerRoutedEventArgs e) => _page.PointerWheel(e);
    private void OnKeyDown(object sender, KeyRoutedEventArgs e) => _page.KeyDown(e);
    private void OnKeyUp(object sender, KeyRoutedEventArgs e) => _page.KeyUp(e);
    private void OnChar(UIElement sender, CharacterReceivedRoutedEventArgs e) => _page.Char(e);
    // The page is blurred explicitly when a chrome control takes focus (see the
    // Address.GotFocus wiring) — not on every LostFocus, which fires transiently.
    private void OnPageLostFocus(object sender, RoutedEventArgs e) { }

    // ---- docked DevTools surface ----
    private void OnDevToolsRegionChanged(object sender, SizeChangedEventArgs e) => _dev.Resize();
    private void OnDevPointerMoved(object sender, PointerRoutedEventArgs e) => _dev.PointerMoved(e);
    private void OnDevPointerExited(object sender, PointerRoutedEventArgs e) => _dev.PointerExited(e);
    private void OnDevPointerPressed(object sender, PointerRoutedEventArgs e) => _dev.PointerPressed(e);
    private void OnDevPointerReleased(object sender, PointerRoutedEventArgs e) => _dev.PointerReleased(e);
    private void OnDevPointerWheel(object sender, PointerRoutedEventArgs e) => _dev.PointerWheel(e);
    private void OnDevKeyDown(object sender, KeyRoutedEventArgs e) => _dev.KeyDown(e);
    private void OnDevKeyUp(object sender, KeyRoutedEventArgs e) => _dev.KeyUp(e);
    private void OnDevChar(UIElement sender, CharacterReceivedRoutedEventArgs e) => _dev.Char(e);
    private void OnDevLostFocus(object sender, RoutedEventArgs e) { }

    // ---- DevTools pane splitter ----
    private bool _draggingSplitter;
    private void OnDevSplitterPressed(object sender, PointerRoutedEventArgs e)
    {
        _draggingSplitter = true;
        DevToolsSplitter.CapturePointer(e.Pointer);
    }
    private void OnDevSplitterMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_draggingSplitter) return;
        var x = e.GetCurrentPoint(Root).Position.X;
        var w = Math.Clamp(Root.ActualWidth - x, 240, Root.ActualWidth - 240);
        DevToolsColumn.Width = new GridLength(w);
    }
    private void OnDevSplitterReleased(object sender, PointerRoutedEventArgs e)
    {
        _draggingSplitter = false;
        DevToolsSplitter.ReleasePointerCapture(e.Pointer);
        _dev.Resize();
    }

    // ---- tab strip ----
    // We build TabViewItems by hand (rather than TabItemsSource + TabItemTemplate)
    // because TabView does not refresh a templated header when the bound model's
    // properties change — the tab title would stay frozen at "New Tab".
    private readonly Dictionary<TabModel, TabViewItem> _tabItems = new();
    private bool _syncingStrip;
    private bool _reorderingStrip;

    private void SyncTabStrip()
    {
        // add missing
        foreach (var t in Vm.Tabs)
            if (!_tabItems.ContainsKey(t))
            {
                var item = BuildTabItem(t);
                _tabItems[t] = item;
                t.PropertyChanged += OnTabModelPropertyChanged;
            }

        // remove stale
        foreach (var kv in _tabItems.Where(kv => !Vm.Tabs.Contains(kv.Key)).ToList())
        {
            kv.Key.PropertyChanged -= OnTabModelPropertyChanged;
            _tabItems.Remove(kv.Key);
        }

        _syncingStrip = true;
        TabStrip.TabItems.Clear();
        foreach (var t in Vm.Tabs) TabStrip.TabItems.Add(_tabItems[t]);
        _syncingStrip = false;

        SelectActiveInStrip();
    }

    private static TabViewItem BuildTabItem(TabModel t)
    {
        var icon = new FontIcon
        {
            Glyph = "",
            FontSize = 12,
            Foreground = s_sandboxText,
            Visibility = t.IsSandbox ? Visibility.Visible : Visibility.Collapsed,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var text = new TextBlock
        {
            Text = t.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(icon);
        panel.Children.Add(text);
        return new TabViewItem { Header = panel, Tag = t };
    }

    private void OnTabModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TabModel t || e.PropertyName != nameof(TabModel.Title)) return;
        if (_tabItems.TryGetValue(t, out var item) &&
            item.Header is StackPanel { Children: [_, TextBlock tb] })
            tb.Text = t.Title;
    }

    // ---- tab strip drag-to-reorder ----
    // TabView owns TabItems as a plain IList when built by hand (not TabItemsSource),
    // but with CanReorderTabs=true it still moves the elements in place on a drag —
    // observable via the underlying IObservableVector, same as ItemsControl.Items.
    private void OnTabItemsReordered(Windows.Foundation.Collections.IObservableVector<object> sender,
        Windows.Foundation.Collections.IVectorChangedEventArgs e)
    {
        if (_syncingStrip || _reorderingStrip) return;
        var order = TabStrip.TabItems.OfType<TabViewItem>()
            .Select(i => i.Tag as TabModel)
            .Where(t => t is not null)
            .Cast<TabModel>()
            .ToList();
        if (order.Count != Vm.Tabs.Count) return; // mid add/remove — ignore, a settle event follows

        _reorderingStrip = true;
        Vm.ReorderTabs(order);
        _reorderingStrip = false;
    }

    private void OnAddTab(TabView sender, object args) => Vm.NewTab();

    private void OnTabClose(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab?.Tag is TabModel t) Vm.CloseTab(t);
    }

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingStrip) return;
        if (TabStrip.SelectedItem is TabViewItem { Tag: TabModel t } && !ReferenceEquals(t, Vm.ActiveTab))
            Vm.ActiveTab = t;
    }

    private void SelectActiveInStrip()
    {
        if (Vm.ActiveTab is { } a && _tabItems.TryGetValue(a, out var item)
            && !ReferenceEquals(TabStrip.SelectedItem, item))
        {
            _syncingStrip = true;
            TabStrip.SelectedItem = item;
            _syncingStrip = false;
        }
    }

    // ---- address bar ----
    private void OnAddressTextChanged(AutoSuggestBox box, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        box.ItemsSource = Vm.Suggest(box.Text).Select(x => x.Url).ToList();
    }
    private void OnSuggestionChosen(AutoSuggestBox box, AutoSuggestBoxSuggestionChosenEventArgs args)
        => box.Text = args.SelectedItem?.ToString() ?? box.Text;
    private void OnAddressSubmitted(AutoSuggestBox box, AutoSuggestBoxQuerySubmittedEventArgs args)
        => Vm.NavigateCommand.Execute(args.QueryText ?? box.Text);
}
