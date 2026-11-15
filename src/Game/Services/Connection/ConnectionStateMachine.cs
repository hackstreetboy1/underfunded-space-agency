namespace Usa.Game.Services.Connection;

public enum ConnectionTransition
{
	None,
	Lost,
	Restored,
}

/// <summary>
/// Debounces heartbeats (SCRIPTING.md 13.4): <see cref="FailuresToLose"/> consecutive failures lose the
/// connection, <see cref="SuccessesToRestore"/> consecutive successes restore it. Starts connected, so a
/// single blip at boot never freezes anything. Pure and deterministic.
/// </summary>
public sealed class ConnectionStateMachine(int failuresToLose = 2, int successesToRestore = 2)
{
	private int _consecutiveFailures;
	private int _consecutiveSuccesses;

	public int FailuresToLose { get; } = failuresToLose;

	public int SuccessesToRestore { get; } = successesToRestore;

	public bool IsLost { get; private set; }

	public ConnectionTransition Observe(bool essentialOk)
	{
		if (essentialOk)
		{
			_consecutiveFailures = 0;
			_consecutiveSuccesses++;
			if (IsLost && _consecutiveSuccesses >= SuccessesToRestore)
			{
				IsLost = false;
				return ConnectionTransition.Restored;
			}
			return ConnectionTransition.None;
		}

		_consecutiveSuccesses = 0;
		_consecutiveFailures++;
		if (!IsLost && _consecutiveFailures >= FailuresToLose)
		{
			IsLost = true;
			return ConnectionTransition.Lost;
		}
		return ConnectionTransition.None;
	}
}
