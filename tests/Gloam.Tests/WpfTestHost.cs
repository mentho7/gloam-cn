using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Gloam.Tests;

/// <summary>Shared STA dispatcher and the one <see cref="Application"/> a process may have.</summary>
/// <remarks>
/// WPF allows a single <see cref="Application"/> per AppDomain, and its resources have thread
/// affinity. Two test classes that each created their own therefore could not both run: the second
/// threw "cannot create more than one System.Windows.Application instance". Creating the thread, the
/// dispatcher and the application once, and marshalling every UI test onto that thread, is the only
/// arrangement where the constraint is satisfied structurally rather than by test ordering luck.
/// </remarks>
internal static class WpfTestHost
{
    private static readonly object Gate = new();
    private static Dispatcher? _dispatcher;
    private static Application? _application;

    /// <summary>The shared application, with the theme tokens merged. Valid inside <see cref="Run"/>.</summary>
    internal static Application Application =>
        _application ?? throw new InvalidOperationException("Application is only available inside Run().");

    internal static void Run(Action body)
    {
        Dispatcher dispatcher = UiDispatcher();

        // Called from inside another Run: already on the right thread, and Dispatcher.Invoke would
        // deadlock waiting on itself.
        if (dispatcher.CheckAccess())
        {
            body();
            return;
        }

        ExceptionDispatchInfo? failure = null;
        dispatcher.Invoke(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });

        failure?.Throw();
    }

    /// <summary>Pumps until <paramref name="until"/> holds, or the budget expires.</summary>
    /// <remarks>
    /// Fixed durations make a test pass on an idle machine and fail on a loaded one. Waiting for the
    /// condition the assertion is about removes the guess; the budget only bounds the failure.
    /// </remarks>
    internal static void PumpUntil(Func<bool> until, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            Pump(TimeSpan.FromMilliseconds(50));
            if (until())
                return;
        }
    }

    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = duration,
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static Dispatcher UiDispatcher()
    {
        lock (Gate)
        {
            if (_dispatcher is not null)
                return _dispatcher;

            Dispatcher? created = null;
            using var ready = new ManualResetEventSlim();

            var thread = new Thread(() =>
            {
                created = Dispatcher.CurrentDispatcher;
                _application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                _application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Gloam;component/Themes/Tokens.xaml"),
                });
                ready.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "gloam-ui-tests",
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            _dispatcher = created ?? throw new InvalidOperationException("UI dispatcher failed to start.");
            return _dispatcher;
        }
    }
}
