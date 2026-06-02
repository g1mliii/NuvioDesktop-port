using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Nuvio.Desktop.ViewModels;

namespace Nuvio.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _boundViewModel;
    private WindowState _preFullscreenState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnbindViewModel();

        _boundViewModel = DataContext as MainWindowViewModel;

        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _boundViewModel.OpenMediaFileRequested += OnOpenMediaFileRequested;
            _boundViewModel.QuitRequested += OnQuitRequested;
            _boundViewModel.UpdateShellMetrics(Bounds.Width > 0 ? Bounds.Width : Width, RenderScaling);

            if (_boundViewModel.IsMacOS)
            {
                // macOS gets a native application menu; the in-window Menu is hidden via IsInWindowMenuVisible.
                NativeMenu.SetMenu(this, BuildNativeMenu(_boundViewModel));
            }
        }
    }

    private void UnbindViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.OpenMediaFileRequested -= OnOpenMediaFileRequested;
            _boundViewModel.QuitRequested -= OnQuitRequested;
        }

        NativeMenu.SetMenu(this, null);
        _boundViewModel = null;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _boundViewModel?.UpdateShellMetrics(e.NewSize.Width, RenderScaling);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsPlayerFullscreen) && _boundViewModel is not null)
        {
            ApplyFullscreen(_boundViewModel.IsPlayerFullscreen);
        }
    }

    private void ApplyFullscreen(bool fullscreen)
    {
        if (fullscreen)
        {
            if (WindowState != WindowState.FullScreen)
            {
                _preFullscreenState = WindowState;
                WindowState = WindowState.FullScreen;
            }
        }
        else if (WindowState == WindowState.FullScreen)
        {
            WindowState = _preFullscreenState;
        }
    }

    private async void OnOpenMediaFileRequested()
    {
        try
        {
            var topLevel = GetTopLevel(this);
            if (topLevel is null || _boundViewModel is null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open media file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Media files")
                    {
                        Patterns = ["*.mp4", "*.mkv", "*.webm", "*.avi", "*.mov", "*.m4v", "*.mp3", "*.flac", "*.m3u8"]
                    },
                    FilePickerFileTypes.All
                ]
            });

            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                await _boundViewModel.PlayLocalFileAsync(path);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Nuvio Desktop open-media-file failed: {ex}");
        }
    }

    private void OnQuitRequested() => Close();

    /// <summary>
    /// Builds the macOS native application menu. Exposed internally so tests can exercise the construction
    /// path on any OS without a real menu bar.
    /// </summary>
    internal static NativeMenu BuildNativeMenu(MainWindowViewModel viewModel)
    {
        var appMenu = new NativeMenu();
        appMenu.Add(new NativeMenuItem("About Nuvio Desktop"));
        appMenu.Add(new NativeMenuItemSeparator());
        appMenu.Add(new NativeMenuItem("Preferences…")
        {
            Command = viewModel.NavigateCommand,
            CommandParameter = Models.DesktopRoute.Settings,
        });
        appMenu.Add(new NativeMenuItemSeparator());
        appMenu.Add(new NativeMenuItem("Quit Nuvio Desktop")
        {
            Command = viewModel.QuitCommand,
            Gesture = viewModel.QuitKeyGesture,
        });

        var fileMenu = new NativeMenu();
        fileMenu.Add(new NativeMenuItem("Open Media File…")
        {
            Command = viewModel.OpenMediaFileCommand,
            Gesture = viewModel.OpenMediaFileKeyGesture,
        });

        var browseMenu = new NativeMenu();
        browseMenu.Add(new NativeMenuItem("Back")
        {
            Command = viewModel.BackCommand,
            Gesture = viewModel.BackKeyGesture,
        });
        browseMenu.Add(new NativeMenuItem("Search")
        {
            Command = viewModel.FocusSearchCommand,
            Gesture = viewModel.SearchKeyGesture,
        });

        var playbackMenu = new NativeMenu();
        playbackMenu.Add(new NativeMenuItem("Play / Pause")
        {
            Command = viewModel.Player.TogglePlayPauseCommand,
            Gesture = viewModel.PlayPauseKeyGesture,
        });
        playbackMenu.Add(new NativeMenuItem("Seek Back")
        {
            Command = viewModel.Player.SeekBackwardCommand,
            Gesture = viewModel.SeekBackwardKeyGesture,
        });
        playbackMenu.Add(new NativeMenuItem("Seek Forward")
        {
            Command = viewModel.Player.SeekForwardCommand,
            Gesture = viewModel.SeekForwardKeyGesture,
        });

        var viewMenu = new NativeMenu();
        viewMenu.Add(new NativeMenuItem("Toggle Fullscreen")
        {
            Command = viewModel.ToggleFullscreenCommand,
            Gesture = viewModel.FullscreenKeyGesture,
        });

        var windowMenu = new NativeMenu();
        windowMenu.Add(new NativeMenuItem($"Search Shortcut: {viewModel.SearchShortcutLabel}") { IsEnabled = false });
        windowMenu.Add(new NativeMenuItem($"Back Shortcut: {viewModel.BackKeyGesture}") { IsEnabled = false });

        var menu = new NativeMenu();
        menu.Add(new NativeMenuItem("Nuvio Desktop") { Menu = appMenu });
        menu.Add(new NativeMenuItem("File") { Menu = fileMenu });
        menu.Add(new NativeMenuItem("Browse") { Menu = browseMenu });
        menu.Add(new NativeMenuItem("Playback") { Menu = playbackMenu });
        menu.Add(new NativeMenuItem("View") { Menu = viewMenu });
        menu.Add(new NativeMenuItem("Window") { Menu = windowMenu });
        return menu;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        try
        {
            if (DataContext is MainWindowViewModel viewModel &&
                viewModel.CanHandleShortcut(e.Key, e.KeyModifiers))
            {
                e.Handled = true;
                await viewModel.HandleShortcutAsync(e.Key, e.KeyModifiers);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Nuvio Desktop shortcut handling failed: {ex}");
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        SizeChanged -= OnSizeChanged;
        DataContextChanged -= OnDataContextChanged;
        UnbindViewModel();

        if (DataContext is IAsyncDisposable disposable)
        {
            DataContext = null;
            try
            {
                await disposable.DisposeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Nuvio Desktop shutdown cleanup failed: {ex}");
            }
        }
    }
}
