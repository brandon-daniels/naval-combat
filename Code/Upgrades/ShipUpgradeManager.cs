using System;
using System.Collections.Generic;
using System.Linq;

namespace NavalCombat;

public sealed class ShipUpgradeManager : Component
{
	[Property] public List<ShipUpgradeDefinition> AvailableUpgrades { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	private readonly Dictionary<string, int> levels = new( StringComparer.OrdinalIgnoreCase );

	public int GetLevel( ShipUpgradeDefinition upgrade )
	{
		if ( upgrade is null ) return 0;
		return levels.TryGetValue( upgrade.StableId, out var level ) ? level : 0;
	}

	public float GetModifier( ShipUpgradeStat stat )
	{
		return AvailableUpgrades.Where( x => x is not null && x.Stat == stat )
			.Sum( x => x.AmountPerLevel * GetLevel( x ) );
	}

	public TransactionResult Purchase( PlayerWallet wallet, ShipUpgradeDefinition upgrade )
	{
		if ( !GameplayAuthority.CanMutate ) return TransactionResult.Rejected( TransactionFailure.NotAuthoritative );
		if ( !wallet.IsValid() || upgrade is null || !AvailableUpgrades.Contains( upgrade ) ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		var level = GetLevel( upgrade );
		if ( level >= Math.Max( 1, upgrade.MaximumLevel ) ) return TransactionResult.Rejected( TransactionFailure.MaximumLevel );
		var cost = upgrade.GetCost( level );
		if ( !wallet.CanAfford( cost ) ) return TransactionResult.Rejected( TransactionFailure.InsufficientFunds );

		wallet.TryDebit( cost );
		levels[upgrade.StableId] = level + 1;
		Revision++;
		return TransactionResult.Accepted( level + 1, cost );
	}
}
