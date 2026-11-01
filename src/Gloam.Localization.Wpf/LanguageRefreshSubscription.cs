using System.Windows.Threading;

namespace Gloam.Localization.Wpf;

/// <summary>Owns a language subscription and delivers refreshes on the consumer's UI thread.</summary>
public sealed class LanguageRefreshSubscription : IDisposable
{
    private readonly ILocalizer _localizer;
    private readonly Action _refresh;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;

    public LanguageRefreshSubscription(ILocalizer localizer, Action refresh)
    {
        _localizer = localizer;
        _refresh = refresh;
        _localizer.CultureChanged += OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, CultureChangedEventArgs e)
    {
        if (_dispatcher.CheckAccess()) Refresh();
        else if (!_dispatcher.HasShutdownStarted)
            _dispatcher.BeginInvoke((Action)Refresh);
    }

    private void Refresh()
    {
        if (!_disposed) _refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _localizer.CultureChanged -= OnCultureChanged;
    }
}
