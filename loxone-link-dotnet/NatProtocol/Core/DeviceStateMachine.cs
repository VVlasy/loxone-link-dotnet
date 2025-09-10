using Microsoft.Extensions.Logging;

namespace loxonelinkdotnet.NatProtocol.Core;

/// <summary>
/// Device states in the Loxone Link NAT protocol
/// </summary>
public enum DeviceState
{
    /// <summary>
    /// Device is offline, requesting NAT assignment
    /// </summary>
    Offline = 0,

    /// <summary>
    /// Device has been assigned a NAT but is parked (not active)
    /// </summary>
    Parked = 1,

    /// <summary>
    /// Device is online and fully operational
    /// </summary>
    Online = 2
}

/// <summary>
/// State change reasons for logging and diagnostics
/// </summary>
public enum StateChangeReason
{
    /// <summary>
    /// Device was reset or started
    /// </summary>
    Reset,

    /// <summary>
    /// NAT assignment received from Miniserver
    /// </summary>
    NatAssignment,

    /// <summary>
    /// Device was parked by Miniserver
    /// </summary>
    Parked,

    /// <summary>
    /// Device was unparked and authorized
    /// </summary>
    Authorized,

    /// <summary>
    /// Communication timeout - going offline
    /// </summary>
    Timeout,

    /// <summary>
    /// Extensions offline command received
    /// </summary>
    ExtensionsOffline
}

/// <summary>
/// Event arguments for state change events
/// </summary>
public class StateChangeEventArgs : EventArgs
{
    public DeviceState PreviousState { get; }
    public DeviceState NewState { get; }
    public StateChangeReason Reason { get; }
    public DateTime Timestamp { get; }

    public StateChangeEventArgs(DeviceState previousState, DeviceState newState, StateChangeReason reason)
    {
        PreviousState = previousState;
        NewState = newState;
        Reason = reason;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// State machine for managing NAT device lifecycle
/// </summary>
public class DeviceStateMachine
{
    private DeviceState _currentState = DeviceState.Offline;
    private readonly ILogger _logger;
    private readonly string _deviceAlias;

    // State-specific timing and counters
    private DateTime _stateEnterTime = DateTime.UtcNow;
    private DateTime _lastOfferTime = DateTime.MinValue;
    private DateTime _lastKeepAliveTime = DateTime.MinValue;
    private int _natRequestCounter = 0;
    private int _offlineCountdown = 0;
    private int _offlineTimeoutSeconds = 15 * 60; // Default 15 minutes

    // Configuration
    private readonly Random _random = new();

    public event EventHandler<StateChangeEventArgs>? StateChanged;

    public DeviceStateMachine(ILogger logger, string deviceAlias)
    {
        _logger = logger;
        _deviceAlias = deviceAlias;
    }

    /// <summary>
    /// Current device state
    /// </summary>
    public DeviceState CurrentState => _currentState;

    /// <summary>
    /// Time when the current state was entered
    /// </summary>
    public DateTime StateEnterTime => _stateEnterTime;

    /// <summary>
    /// Duration in current state
    /// </summary>
    public TimeSpan TimeInCurrentState => DateTime.UtcNow - _stateEnterTime;

    /// <summary>
    /// Set the offline timeout from configuration
    /// </summary>
    public void SetOfflineTimeout(int timeoutSeconds)
    {
        _offlineTimeoutSeconds = Math.Max(60, timeoutSeconds); // Minimum 1 minute
        _offlineCountdown = _offlineTimeoutSeconds;
        _logger?.LogDebug($"[{_deviceAlias}] Offline timeout set to {_offlineTimeoutSeconds}s ({_offlineTimeoutSeconds / 60.0:F1}min)");
    }

    /// <summary>
    /// Transition to a new state
    /// </summary>
    public void TransitionTo(DeviceState newState, StateChangeReason reason)
    {
        if (_currentState == newState)
        {
            _logger?.LogDebug($"[{_deviceAlias}] Already in state {newState} - ignoring transition");
            return;
        }

        var previousState = _currentState;
        _currentState = newState;
        _stateEnterTime = DateTime.UtcNow;
        _lastKeepAliveTime = DateTime.UtcNow + TimeSpan.FromMinutes(10);


        // Reset state-specific counters and timers
        switch (newState)
        {
            case DeviceState.Offline:
                _natRequestCounter = 0;
                _lastOfferTime = DateTime.MinValue;
                break;

            case DeviceState.Parked:
                _natRequestCounter = 0;
                _offlineCountdown = _offlineTimeoutSeconds;
                break;

            case DeviceState.Online:
                _natRequestCounter = 0;
                _offlineCountdown = _offlineTimeoutSeconds;
                _lastKeepAliveTime = DateTime.UtcNow;
                break;
        }

        _logger?.LogInformation($"[{_deviceAlias}] State transition: {previousState} → {newState} (Reason: {reason})");

        // Fire event
        StateChanged?.Invoke(this, new StateChangeEventArgs(previousState, newState, reason));
    }

    /// <summary>
    /// Reset to offline state
    /// </summary>
    public void Reset()
    {
        TransitionTo(DeviceState.Offline, StateChangeReason.Reset);
    }

    /// <summary>
    /// Handle NAT assignment
    /// </summary>
    public void HandleNatAssignment(bool isParked)
    {
        if (isParked)
        {
            TransitionTo(DeviceState.Parked, StateChangeReason.NatAssignment);
        }
        else
        {
            TransitionTo(DeviceState.Online, StateChangeReason.NatAssignment);
        }
    }

    /// <summary>
    /// Handle authorization completion
    /// </summary>
    public void HandleAuthorization()
    {
        if (_currentState == DeviceState.Parked)
        {
            TransitionTo(DeviceState.Online, StateChangeReason.Authorized);
        }
    }

    /// <summary>
    /// Handle extensions offline command
    /// </summary>
    public void HandleDeviceOffline()
    {
        // Don't transition to offline, but reset offer timing
        _lastOfferTime = DateTime.MinValue;
        _logger?.LogInformation($"[{_deviceAlias}] Extensions offline - resetting offer timing");
    }

    /// <summary>
    /// Reset communication timeout
    /// </summary>
    public void ResetTimeout()
    {
        _offlineCountdown = _offlineTimeoutSeconds;
    }

    /// <summary>
    /// Check if device should send NAT offer request
    /// </summary>
    public bool ShouldSendOffer(out TimeSpan nextOfferDelay)
    {
        nextOfferDelay = TimeSpan.Zero;

        if (_currentState != DeviceState.Offline)
            return false;

        var now = DateTime.UtcNow;
        var timeSinceLastOffer = now - _lastOfferTime;

        // Calculate delay based on attempt counter (exponential backoff)
        var baseDelayMs = _natRequestCounter <= 2 ? 100 + _random.Next(50) :
                         _natRequestCounter <= 9 ? 500 + _random.Next(500) :
                         2000 + _random.Next(1000);

        var requiredDelay = TimeSpan.FromMilliseconds(baseDelayMs);

        if (timeSinceLastOffer >= requiredDelay || _lastOfferTime == DateTime.MinValue)
        {
            _lastOfferTime = now;
            _natRequestCounter++;
            return true;
        }

        nextOfferDelay = requiredDelay - timeSinceLastOffer;
        return false;
    }

    /// <summary>
    /// Check if device should send keep-alive
    /// </summary>
    public bool ShouldSendKeepAlive(TimeSpan interval)
    {
        if (_currentState != DeviceState.Online && _currentState != DeviceState.Parked)
            return false;

        var now = DateTime.UtcNow;
        if (now - _lastKeepAliveTime >= interval)
        {
            _lastKeepAliveTime = now;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Update state machine (call regularly from timer)
    /// </summary>
    public void Update()
    {
        // Handle timeout countdown for parked/online states
        if ((_currentState == DeviceState.Parked || _currentState == DeviceState.Online) && _offlineCountdown > 0)
        {
            _offlineCountdown--;

            // Send keep-alive when countdown reaches 1/10 of timeout
            if (_offlineCountdown == _offlineTimeoutSeconds / 10)
            {
                _logger?.LogDebug($"[{_deviceAlias}] Timeout warning - should send keep-alive");
            }
            else if (_offlineCountdown <= 0)
            {
                _logger?.LogWarning($"[{_deviceAlias}] Communication timeout - going offline");
                TransitionTo(DeviceState.Offline, StateChangeReason.Timeout);
            }
        }
    }

    /// <summary>
    /// Get state information for diagnostics
    /// </summary>
    public string GetStateInfo()
    {
        var timeInState = TimeInCurrentState;
        return $"State: {_currentState}, Duration: {timeInState:mm\\:ss}, " +
               $"Offers: {_natRequestCounter}, Timeout: {_offlineCountdown}s";
    }
}