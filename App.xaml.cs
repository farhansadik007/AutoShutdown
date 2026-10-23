using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoShutdown.Core;
using AutoShutdown.Models;
using AutoShutdown.ViewModels;

namespace AutoShutdown;

public partial class App : Application
{
    private const string MutexName = @"Local\AutoShutdown.SingleInstance";

    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Dev helper: bake the app mark into a real multi-resolution .ico and exit.
        var exportIconPath = GetValue(e.Args, "--export-icon");
        if (exportIconPath is not null)
        {
            AppIcon.ExportIcon(exportIconPath);
            Log.Start();
            Log.Write($"Icon exported: {exportIconPath}");
            Environment.Exit(0);
        }

        // Dev helper: generate the Microsoft Store / MSIX artwork battery.
        var exportAssetsDir = GetValue(e.Args, "--export-store-assets");
        if (exportAssetsDir is not null)
        {
            Log.Start();
            StoreAssets.ExportAll(exportAssetsDir);
            Environment.Exit(0);
        }

        // Dev helper: pack a prepared layout folder into an MSIX (OPC) package.
        var packIndex = Array.IndexOf(e.Args, "--pack-msix");
        if (packIndex >= 0 && packIndex + 2 < e.Args.Length)
        {
            var layoutDir = e.Args[packIndex + 1];
            var msixPath = e.Args[packIndex + 2];
            Log.Start();
            MsixPacker.Pack(layoutDir, msixPath);
            Log.Write($"MSIX packed: {msixPath}");
            Environment.Exit(0);
        }

        // Screenshot runs are development-only and must not fight with a live
        // instance over the single-instance mutex.
        var screenshotPath = GetValue(e.Args, "--shot");

        if (screenshotPath is null)
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                MessageBox.Show(
                    "Shutdown is already running.",
                    "AutoShutdown",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        Log.Start();
        Log.Write("AutoShutdown starting");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("Fatal: " + args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("Unobserved task exception: " + args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);

        var viewModel = new MainViewModel(SettingsStore.Load());
        var window = new MainWindow(viewModel);

        MainWindow = window;
        window.Show();
        Log.Write("Window shown");

        var demo = HasFlag(e.Args, "--demo") || GetValue(e.Args, "--demo-seconds") is not null;
        if (demo)
        {
            // Safe preview mode: a short countdown that performs no power action
            // and never overwrites the real saved settings.
            viewModel.PersistSettings = false;

            var seconds = int.TryParse(GetValue(e.Args, "--demo-seconds"), out var custom) ? custom : 75;
            Log.Write($"Demo mode: starting {seconds}s countdown with no action");

            var demoTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(900)
            };
            demoTimer.Tick += (_, _) =>
            {
                demoTimer.Stop();
                viewModel.StartWith(TimeSpan.FromSeconds(seconds), PowerActionOption.Nothing);
            };
            demoTimer.Start();
        }

        if (screenshotPath is not null)
        {
            var delay = int.TryParse(GetValue(e.Args, "--shot-delay"), out var customDelay) ? customDelay : 2600;
            ScheduleScreenshot(window, screenshotPath, delay, e.Args);
        }
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? GetValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// Development helper: renders the window visual to a PNG and exits.
    /// Works even when the desktop is not being captured by the OS.
    /// </summary>
    private void ScheduleScreenshot(Window window, string path, int delayMilliseconds, string[] args)
    {
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
        {
            Interval = TimeSpan.FromMilliseconds(delayMilliseconds)
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            try
            {
                var root = (FrameworkElement)window.Content;
                root.UpdateLayout();

                var zoom = double.TryParse(GetValue(args, "--shot-zoom"), out var parsedZoom) && parsedZoom > 0
                    ? parsedZoom
                    : 1.0;

                var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * zoom));
                var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * zoom));

                var bitmap = new RenderTargetBitmap(width, height, 96 * zoom, 96 * zoom, PixelFormats.Pbgra32);
                bitmap.Render(root);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using var stream = File.Create(path);
                encoder.Save(stream);

                Log.Write($"Screenshot saved: {path} ({width}x{height})");
            }
            catch (Exception ex)
            {
                Log.Write("Screenshot failed: " + ex);
            }

            // Hard exit: window-level close guards must not keep this dev run alive.
            Environment.Exit(0);
        };

        timer.Start();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write("Unhandled: " + e.Exception);

        MessageBox.Show(
            "Something went wrong:\n\n" + e.Exception.Message,
            "AutoShutdown",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // Already released.
            }
        }

        _mutex?.Dispose();
        Log.Write("Shutdown exited");
        base.OnExit(e);
    }
}
