using System;

namespace NavalCombat;

public sealed class ShipHealth : Component
{
	[Property] public int MaximumHealth { get; set; } = 1000;
	[Sync( SyncFlags.FromHost )] public int Health { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsDefeated { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	protected override void OnStart()
	{
		if ( GameplayAuthority.CanMutate && Revision == 0 ) ResetHealth();
	}

	public bool ApplyDamage( int amount )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated || amount <= 0 ) return false;
		Health = Math.Max( 0, Health - amount );
		IsDefeated = Health == 0;
		Revision++;
		return true;
	}

	public bool Repair( int amount )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated || amount <= 0 || Health >= MaximumHealth ) return false;
		Health = Math.Min( Math.Max( 1, MaximumHealth ), Health + amount );
		Revision++;
		return true;
	}

	public void ResetHealth()
	{
		if ( !GameplayAuthority.CanMutate ) return;
		Health = Math.Max( 1, MaximumHealth );
		IsDefeated = false;
		Revision++;
	}
}
