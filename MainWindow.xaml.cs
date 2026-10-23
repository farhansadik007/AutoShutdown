using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AutoShutdown.Core;
using AutoShutdown.ViewModels;

namespace AutoShutdown;

public partial class MainWindow : Window
{
    // Flat timer colours: white clock face; countdown pie heats from orange to red.
    private static readonly Color DiscColor = Colors.White;
    private static readonly Color WedgeColor = Color.FromRgb(0x43, 0x76, 0x6C);
    private static readonly Color CountdownStartColor = Color.FromRgb(0xFF, 0x98, 0x00);
    private static readonly Color CountdownEndColor = Color.FromRgb(0xB8, 0x00, 0x00);

    private readonly MainViewModel _vm;

    private string _lastTimeText = string.Empty;

    // Window shape: full-height card at rest, compact card while counting down.
    private bool _compact;

    private const double PortraitHeight = 690;
    private const double CompactHeight = 352;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _vm = viewModel;
        DataContext = viewModel;
        Icon = AppIcon.WindowIcon;

        Ring.WedgeBrush = new SolidColorBrush(WedgeColor);
        Ring.TrackBrush = new SolidColorBrush(DiscColor);

        PreviewRing.WedgeBrush = new SolidColorBrush(WedgeColor);
        PreviewRing.TrackBrush = new SolidColorBrush(DiscColor);

        _vm.Tick += OnTick;
        _vm.CountdownStarted += OnStarted;
        _vm.CountdownStopped += OnStopped;
        _vm.CountdownFinished += OnFinished;
        _vm.Failed += OnFailed;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        SourceInitialized += (_, _) => UpdateShellClip();
    }

    // ---------------------------------------------------------------- rendering

    private static Color CountdownColorFor(double heat)
    {
        // heat: 0 just started -> 1 about to finish.
        if (heat <= 0.55)
        {
            return WedgeColor; // calm home green while the pie is still fat
        }

        if (heat <= 0.85)
        {
            return Lerp(WedgeColor, CountdownStartColor, (heat - 0.55) / 0.30);
        }

        return Lerp(CountdownStartColor, CountdownEndColor, Math.Min(1.0, (heat - 0.85) / 0.15));
    }

    private static Color Lerp(Color from, Color to, double t)
        => Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));

    private void OnTick(TimeSpan remaining, double progress)
    {
        Ring.Progress = progress;

        // Countdown pie heats up: home green while plenty is left, then orange,
        // finally deep red as zero approaches.
        Ring.WedgeBrush = new SolidColorBrush(CountdownColorFor(1.0 - progress));

        var text = FormatRemaining(remaining);
        if (text != _lastTimeText)
        {
            _lastTimeText = text;
            TimeText.Text = text;
        }
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    // ---------------------------------------------------------------- transitions

    private void OnStarted()
    {
        _lastTimeText = string.Empty;

        HideOverlay(ConfirmOverlay);
        HideOverlay(FinishOverlay);

        Topmost = true;

        SetupView.IsHitTestVisible = false;
        SetupView.Visibility = Visibility.Collapsed;

        CountdownView.Visibility = Visibility.Visible;
        CountdownView.IsHitTestVisible = true;
        CountdownView.Opacity = 1;

        Ring.Progress = 1;

        // Collapse into the compact card while the countdown runs.
        ApplyCompactLayout();
        MorphHeight(CompactHeight);
    }

    private void OnStopped()
    {
        Topmost = false;
        HideOverlay(ConfirmOverlay);
        HideOverlay(FinishOverlay);

        CountdownView.IsHitTestVisible = false;
        CountdownView.Visibility = Visibility.Collapsed;
        CountdownView.Opacity = 1;

        SetupView.Visibility = Visibility.Visible;
        SetupView.IsHitTestVisible = true;
        SetupView.Opacity = 1;

        Ring.Progress = 1;
        Ring.WedgeBrush = new SolidColorBrush(WedgeColor);

        // Morph back to the full-height setup window.
        ExitCompactLayout();
        MorphHeight(PortraitHeight);
    }

    private void OnFinished()
    {
        FinishHeadline.Text = _vm.FinishHeadline;
        FinishDetail.Text = _vm.FinishDetail;

        CountdownView.IsHitTestVisible = false;
        CountdownView.Visibility = Visibility.Collapsed;
        CountdownView.Opacity = 1;

        FinishOverlay.Visibility = Visibility.Visible;
        FinishOverlay.IsHitTestVisible = true;
        FinishOverlay.Opacity = 1;
    }

    private void OnFailed(string message)
    {
        MessageBox.Show(this, message, "AutoShutdown", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ConfirmVisible))
        {
            UpdateConfirmOverlay();
        }
    }

    private void UpdateConfirmOverlay()
    {
        ConfirmOverlay.Opacity = 1;

        if (_vm.ConfirmVisible)
        {
            ConfirmOverlay.Visibility = Visibility.Visible;
            ConfirmOverlay.IsHitTestVisible = true;
            return;
        }

        ConfirmOverlay.IsHitTestVisible = false;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
    }

    private static void HideOverlay(UIElement overlay)
    {
        overlay.BeginAnimation(UIElement.OpacityProperty, null);
        overlay.Opacity = 1;
        overlay.Visibility = Visibility.Collapsed;
        overlay.IsHitTestVisible = false;
    }

    // ---------------------------------------------------------------- square morph

    /// <summary>
    /// Compact countdown geometry: smaller pie, smaller digits, no header or Esc hint.
    /// Applied before the window resize so the content already fits its target.
    /// </summary>
    private void ApplyCompactLayout()
    {
        _compact = true;

        CountdownHeader.Visibility = Visibility.Collapsed;
        CancelHost.Margin = new Thickness(0, 12, 0, 0);
        CountdownView.Margin = new Thickness(0, 46, 0, 12);

        RingHost.Width = 240;
        RingHost.Height = 240;
        RingHost.Margin = new Thickness(0);

        TimeText.FontSize = 46;
        ActionText.FontSize = 11;
        ActionText.Margin = new Thickness(0, 5, 0, 0);
    }

    private void ExitCompactLayout()
    {
        _compact = false;

        CountdownHeader.Visibility = Visibility.Visible;
        CancelHost.Margin = new Thickness(0, 26, 0, 0);
        CountdownView.Margin = new Thickness(24, 50, 24, 20);

        RingHost.Width = 300;
        RingHost.Height = 300;
        RingHost.Margin = new Thickness(0, 14, 0, 0);

        TimeText.FontSize = 62;
        ActionText.FontSize = 12;
        ActionText.Margin = new Thickness(0, 10, 0, 0);
    }

    /// <summary>
    /// Smooth single-phase morph: the inner shell border collapses while the
    /// top follows so the countdown stays visibly centred. The OS window keeps
    /// a constant size - only WPF layout animates, so no layered-window resize
    /// jank. The width never changes.
    /// </summary>
    private void MorphHeight(double toHeight)
    {
        var fromHeight = Shell.ActualHeight;
        if (Math.Abs(fromHeight - toHeight) < 0.5)
        {
            return;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(320);

        var topFrom = ResolveWindowTop();
        var topTo = topFrom + (fromHeight - toHeight) / 2;

        var shellAnim = new DoubleAnimation(fromHeight, toHeight, duration) { EasingFunction = ease };
        var topAnim = new DoubleAnimation(topFrom, topTo, duration) { EasingFunction = ease };

        shellAnim.Completed += (_, _) =>
        {
            Shell.BeginAnimation(HeightProperty, null);
            Shell.Height = toHeight;
        };
        topAnim.Completed += (_, _) => CommitValue(TopProperty, topTo);

        Shell.BeginAnimation(HeightProperty, shellAnim);
        BeginAnimation(TopProperty, topAnim);
    }

    private void CommitValue(DependencyProperty property, double value)
    {
        BeginAnimation(property, null);
        SetValue(property, value);
    }

    // WindowStartupLocation can leave Top as NaN until the window moves;
    // fall back to the centre of the work area in that case.
    private double ResolveWindowTop()
    {
        if (!double.IsNaN(Top))
        {
            return Top;
        }

        var area = SystemParameters.WorkArea;
        return area.Top + Math.Max(0, (area.Height - ActualHeight) / 2);
    }

    // ---------------------------------------------------------------- chrome

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Support_Click(object sender, RoutedEventArgs e)
    {
        var link = "https://www.supportkori.com/farhansadik007";
        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            Log.Write("Support link opened: " + link);
        }
        catch (Exception ex)
        {
            Log.Write("Support link failed: " + ex.Message);
            OnFailed("Could not open the support page.");
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_vm.IsCompleting)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_vm.ConfirmVisible)
            {
                _vm.DismissConfirmCommand.Execute(null);
            }
            else if (_vm.IsRunning)
            {
                _vm.RequestCancelCommand.Execute(null);
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter
            && !_vm.IsRunning
            && !_vm.ConfirmVisible
            && FinishOverlay.Visibility != Visibility.Visible
            && _vm.CanStart)
        {
            _vm.StartCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsCompleting)
        {
            e.Cancel = true;
            return;
        }

        if (_vm.IsRunning && !_vm.ConfirmVisible)
        {
            e.Cancel = true;
            _vm.RequestCancelCommand.Execute(null);
            return;
        }

        _vm.Dispose();
        base.OnClosing(e);
    }

    // ---------------------------------------------------------------- window shape

    private void Shell_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateShellClip();

    private void UpdateShellClip()
    {
        if (Shell.ActualWidth <= 0 || Shell.ActualHeight <= 0)
        {
            return;
        }

        // Softly rounded corners on the shell border; they follow the animated
        // height so the compact card keeps rounded bottom corners too.
        Shell.Clip = new RectangleGeometry(
            new Rect(0, 0, Shell.ActualWidth, Shell.ActualHeight),
            16,
            16);
        Shell.CornerRadius = new CornerRadius(16);
        ShellEdge.CornerRadius = new CornerRadius(16);
    }
}
