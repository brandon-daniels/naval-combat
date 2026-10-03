namespace NavalCombat;

/// <summary>Shared authority check for gameplay state with real value.</summary>
public static class GameplayAuthority
{
	public static bool CanMutate => !Networking.IsActive || Networking.IsHost;
}
