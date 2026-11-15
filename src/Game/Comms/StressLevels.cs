using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Comms;

/// <summary>Narration stress from the GDD 5.5 health bands.</summary>
public static class StressLevels
{
	public const double StableMinHealth = 80;
	public const double CriticalMaxHealth = 35;

	/// <summary>Stable → calm; Stressed and Impaired → tense; Critical → critical.</summary>
	public static StressLevel FromHealth(double healthPercent) => healthPercent switch
	{
		>= StableMinHealth => StressLevel.Calm,
		>= CriticalMaxHealth => StressLevel.Tense,
		_ => StressLevel.Critical,
	};
}
