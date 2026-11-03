namespace Gloam.Core;

public enum OutputApplyStatus { Pending, Applied, Unchanged, Bypassed, Cancelled, Failed }
public enum OutputPolicySource { Display, Preview, NightSchedule, ApplicationExclusion, Game, ManualDaylight }

/// <summary>Requested output and the API execution receipt, not a physical measurement.</summary>
public sealed record DisplayOutputState(long Revision, GammaMode Gamma, int Kelvin,
    OutputPolicySource Source, OutputApplyStatus Status);

/// <summary>Rejects late receipts from superseded requests.</summary>
public sealed class DisplayOutputStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DisplayOutputState> _states = new(StringComparer.OrdinalIgnoreCase);
    private long _revision;

    public DisplayOutputState Begin(string device, GammaMode gamma, int kelvin, OutputPolicySource source)
    {
        lock (_gate)
        {
            var state = new DisplayOutputState(++_revision, gamma, kelvin, source, OutputApplyStatus.Pending);
            _states[device] = state;
            return state;
        }
    }

    public void Complete(string device, long revision, OutputApplyStatus status)
    {
        lock (_gate)
            if (_states.TryGetValue(device, out var state) && state.Revision == revision)
                _states[device] = state with { Status = status };
    }

    public DisplayOutputState? Get(string device)
    {
        lock (_gate) return _states.GetValueOrDefault(device);
    }
}
