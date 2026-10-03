using System;

namespace NavalCombat;

public sealed class PlayerWallet : Component
{
	[Property] public int StartingBalance { get; set; } = 500;
	[Sync( SyncFlags.FromHost )] public int Balance { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	protected override void OnStart()
	{
		if ( GameplayAuthority.CanMutate && Revision == 0 )
		{
			Balance = Math.Max( 0, StartingBalance );
			Revision = 1;
		}
	}

	public bool CanAfford( int amount ) => amount >= 0 && Balance >= amount;
	public bool CanCredit( int amount ) => amount >= 0 && Balance <= int.MaxValue - amount;

	public bool TryDebit( int amount )
	{
		if ( !GameplayAuthority.CanMutate || !CanAfford( amount ) ) return false;
		Balance -= amount;
		Revision++;
		return true;
	}

	public bool TryCredit( int amount )
	{
		if ( !GameplayAuthority.CanMutate || !CanCredit( amount ) ) return false;
		Balance += amount;
		Revision++;
		return true;
	}
}
