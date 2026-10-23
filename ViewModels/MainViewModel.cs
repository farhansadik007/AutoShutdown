using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using AutoShutdown.Core;
using AutoShutdown.Models;

namespace AutoShutdown.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly CountdownEngine _engine = new();
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;

    private bool _isRunning;
    private bool _confirmVisible;
    private bool _completed;
    private bool _disposed;
    private bool _isCompleting;
    private DateTime? _targetAt;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(30)
        };
        _timer.Tick += (_, _) => OnTick();

        StartCommand = new RelayCommand(Start);
        RequestCancelCommand = new RelayCommand(RequestCancel);
        ConfirmCancelCommand = new RelayCommand(ConfirmCancel);
        DismissConfirmCommand = new RelayCommand(DismissConfirm);
        AdjustCommand = new RelayCommand(Adjust);
        PresetCommand = new RelayCommand(ApplyPreset);
        AddFiveMinutesCommand = new RelayCommand(AddFiveMinutes);
    }

    /// <summary>Raised every animation frame while a countdown is live.</summary>
    public event Action<TimeSpan, double>? Tick;

    public event Action? CountdownStarted;

    public event Action? CountdownStopped;

    public event Action? CountdownFinished;

    public event Action<string>? Failed;

    public ICommand StartCommand { get; }

    public ICommand RequestCancelCommand { get; }

    public ICommand ConfirmCancelCommand { get; }

    public ICommand DismissConfirmCommand { get; }

    public ICommand AdjustCommand { get; }

    public ICommand PresetCommand { get; }

    public ICommand AddFiveMinutesCommand { get; }

    /// <summary>Set to false for demo runs so screenshots never disturb real settings.</summary>
    public bool PersistSettings { get; set; } = true;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (Set(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanStart));
            }
        }
    }

    public bool ConfirmVisible
    {
        get => _confirmVisible;
        private set => Set(ref _confirmVisible, value);
    }

    /// <summary>True once the countdown hits zero and the action is being carried out.</summary>
    public bool IsCompleting
    {
        get => _isCompleting;
        private set => Set(ref _isCompleting, value);
    }

    public bool CanStart => !IsRunning && Total > TimeSpan.Zero;

    public TimeSpan Total => new(_settings.Hours, _settings.Minutes, _settings.Seconds);

    public int Hours
    {
        get => _settings.Hours;
        set
        {
            var clamped = Math.Clamp(value, 0, 23);
            if (_settings.Hours == clamped)
            {
                return;
            }

            _settings.Hours = clamped;
            OnTimeChanged();
        }
    }

    public int Minutes
    {
        get => _settings.Minutes;
        set
        {
            var clamped = Math.Clamp(value, 0, 59);
            if (_settings.Minutes == clamped)
            {
                return;
            }

            _settings.Minutes = clamped;
            OnTimeChanged();
        }
    }

    public int Seconds
    {
        get => _settings.Seconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 59);
            if (_settings.Seconds == clamped)
            {
                return;
            }

            _settings.Seconds = clamped;
            OnTimeChanged();
        }
    }

    public PowerActionOption Action
    {
        get => _settings.Action;
        set
        {
            if (_settings.Action == value)
            {
                return;
            }

            _settings.Action = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SetupActionText));
            OnPropertyChanged(nameof(CountdownCaption));
            OnPropertyChanged(nameof(CountdownActionText));
            OnPropertyChanged(nameof(FinishHeadline));
            OnPropertyChanged(nameof(FinishDetail));
        }
    }

    public string HoursText => Hours.ToString("00");

    public string MinutesText => Minutes.ToString("00");

    public string SecondsText => Seconds.ToString("00");

    public string SetupTimeText => Format(Total, alwaysHours: false);

    public string SetupActionText => ActionLabel(Action).ToUpperInvariant();

    public string CountdownCaption => Action switch
    {
        PowerActionOption.Shutdown => "SHUTTING DOWN IN",
        PowerActionOption.Restart => "RESTARTING IN",
        PowerActionOption.Sleep => "SLEEPING IN",
        PowerActionOption.Hibernate => "HIBERNATING IN",
        PowerActionOption.Lock => "LOCKING IN",
        _ => "TIME REMAINING"
    };

    public string CountdownActionText => ActionLabel(Action).ToUpperInvariant();

    public string TargetTimeText => _targetAt is { } target
        ? target.ToString("h:mm tt")
        : string.Empty;

    public string FinishHeadline => Action switch
    {
        PowerActionOption.Shutdown => "Goodnight.",
        PowerActionOption.Restart => "See you in a moment.",
        PowerActionOption.Sleep => "Goodnight.",
        PowerActionOption.Hibernate => "Goodnight.",
        PowerActionOption.Lock => "Locking up.",
        _ => "Time's up."
    };

    public string FinishDetail => Action switch
    {
        PowerActionOption.Shutdown => "Shutting Windows down.",
        PowerActionOption.Restart => "Restarting Windows.",
        PowerActionOption.Sleep => "Putting the machine to sleep.",
        PowerActionOption.Hibernate => "Hibernating the machine.",
        PowerActionOption.Lock => "Locking the workstation.",
        _ => "Nothing else to do."
    };

    public void StartWith(TimeSpan total, PowerActionOption action)
    {
        _settings.Hours = (int)total.TotalHours;
        _settings.Minutes = total.Minutes;
        _settings.Seconds = total.Seconds;
        _settings.Action = action;

        OnTimeChanged();
        OnPropertyChanged(nameof(Action));
        OnPropertyChanged(nameof(SetupActionText));
        OnPropertyChanged(nameof(CountdownCaption));
        OnPropertyChanged(nameof(CountdownActionText));
        OnPropertyChanged(nameof(FinishHeadline));
        OnPropertyChanged(nameof(FinishDetail));

        Start();
    }

    private void Start()
    {
        if (!CanStart)
        {
            return;
        }

        if (PersistSettings)
        {
            SettingsStore.Save(_settings);
        }

        _completed = false;
        ConfirmVisible = false;
        _engine.Start(Total);
        _targetAt = DateTime.Now + Total;
        IsRunning = true;
        OnPropertyChanged(nameof(TargetTimeText));

        Log.Write($"Countdown started: {Total} with action {Action}");
        CountdownStarted?.Invoke();
        _timer.Start();
        OnTick();
    }

    private void RequestCancel() => ConfirmVisible = true;

    private void AddFiveMinutes()
    {
        if (!IsRunning)
        {
            return;
        }

        _engine.Add(TimeSpan.FromMinutes(5));

        if (_targetAt is { } target)
        {
            _targetAt = target + TimeSpan.FromMinutes(5);
            OnPropertyChanged(nameof(TargetTimeText));
        }

        Log.Write("Countdown extended by 5 minutes");
        OnTick();
    }

    private void DismissConfirm() => ConfirmVisible = false;

    private void ConfirmCancel()
    {
        ConfirmVisible = false;
        _timer.Stop();
        _engine.Stop();
        IsRunning = false;
        _targetAt = null;
        OnPropertyChanged(nameof(TargetTimeText));

        Log.Write("Countdown cancelled by user");
        CountdownStopped?.Invoke();
    }

    private void OnTick()
    {
        if (!_engine.IsRunning)
        {
            return;
        }

        var remaining = _engine.Remaining;
        Tick?.Invoke(remaining, _engine.Progress);

        if (remaining <= TimeSpan.Zero)
        {
            _ = CompleteAsync();
        }
    }

    private async Task CompleteAsync()
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _timer.Stop();
        _engine.Stop();
        IsRunning = false;
        IsCompleting = true;

        Log.Write("Countdown reached zero");
        CountdownFinished?.Invoke();

        // Let the finish animation breathe before Windows takes over.
        await Task.Delay(1500).ConfigureAwait(true);

        var result = await Task.Run(() => PowerService.Execute(Action)).ConfigureAwait(true);

        if (!result.Success)
        {
            Failed?.Invoke(result.Error ?? "The power action failed.");
        }

        if (!result.Success || Action is PowerActionOption.Nothing or PowerActionOption.Lock)
        {
            ReturnToSetup();
            return;
        }

        // Windows usually takes over within a second. If the request was accepted
        // but the shutdown was vetoed, don't leave the app stuck on the fade.
        await Task.Delay(5000).ConfigureAwait(true);
        ReturnToSetup();
    }

    private void ReturnToSetup()
    {
        _completed = false;
        ConfirmVisible = false;
        IsRunning = false;
        IsCompleting = false;
        _targetAt = null;
        OnPropertyChanged(nameof(TargetTimeText));
        CountdownStopped?.Invoke();
    }

    private void Adjust(object? parameter)
    {
        switch (parameter as string)
        {
            case "h+":
                Hours = Hours >= 23 ? 0 : Hours + 1;
                break;
            case "h-":
                Hours = Hours <= 0 ? 23 : Hours - 1;
                break;
            case "m+":
                Minutes = Minutes >= 59 ? 0 : Minutes + 1;
                break;
            case "m-":
                Minutes = Minutes <= 0 ? 59 : Minutes - 1;
                break;
            case "s+":
                Seconds = Seconds >= 59 ? 0 : Seconds + 1;
                break;
            case "s-":
                Seconds = Seconds <= 0 ? 59 : Seconds - 1;
                break;
        }
    }

    private void ApplyPreset(object? parameter)
    {
        if (!int.TryParse(parameter as string, out var seconds))
        {
            return;
        }

        Hours = seconds / 3600;
        Minutes = seconds % 3600 / 60;
        Seconds = seconds % 60;
    }

    private void OnTimeChanged()
    {
        OnPropertyChanged(nameof(Hours));
        OnPropertyChanged(nameof(Minutes));
        OnPropertyChanged(nameof(Seconds));
        OnPropertyChanged(nameof(HoursText));
        OnPropertyChanged(nameof(MinutesText));
        OnPropertyChanged(nameof(SecondsText));
        OnPropertyChanged(nameof(SetupTimeText));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(CanStart));
    }

    private static string Format(TimeSpan value, bool alwaysHours)
    {
        var totalSeconds = (int)Math.Ceiling(value.TotalSeconds);
        var span = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));

        return alwaysHours || span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    private static string ActionLabel(PowerActionOption action) => action switch
    {
        PowerActionOption.Shutdown => "Shut down",
        PowerActionOption.Restart => "Restart",
        PowerActionOption.Sleep => "Sleep",
        PowerActionOption.Hibernate => "Hibernate",
        PowerActionOption.Lock => "Lock",
        _ => "Nothing"
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _engine.Stop();

        if (PersistSettings)
        {
            SettingsStore.Save(_settings);
        }

        Log.Write("Application shutting down");
    }
}
