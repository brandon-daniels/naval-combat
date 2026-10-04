using System;
using System.Collections.Generic;
using System.Linq;

namespace NavalCombat;

public sealed class ShipUpgradeManager : Component
{
	[Property] public List<ShipUpgradeDefinition> AvailableUpgrades { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	[Sync( SyncFlags.FromHost )] public int CargoLevel { get; private set; }
	[Sync( SyncFlags.FromHost )] public int HullLevel { get; private set; }
	[Sync( SyncFlags.FromHost )] public int RiggingLevel { get; private set; }
	private int baseCapacity;
	private int baseHealth;
	private float baseSpeed;
	private bool initialized;

	protected override void OnUpdate()
	{
		var cargo = GetComponent<CargoHold>();
		var health = GetComponent<ShipHealth>();
		var ship = GetComponent<ArcadeShip>();
		if ( !cargo.IsValid() || !health.IsValid() || !ship.IsValid() ) return;
		if ( !initialized )
		{
			baseCapacity = cargo.Capacity;
			baseHealth = health.MaximumHealth;
			baseSpeed = ship.ForwardSpeed;
			initialized = true;
		}
		cargo.Capacity = baseCapacity + (int)GetModifier( ShipUpgradeStat.CargoCapacity );
		health.MaximumHealth = baseHealth + (int)GetModifier( ShipUpgradeStat.HullHealth );
		ship.ForwardSpeed = baseSpeed * (1 + GetModifier( ShipUpgradeStat.SailingPerformance ));
	}

	public int GetLevel( ShipUpgradeDefinition upgrade )
	{
		if ( upgrade is null ) return 0;
		return upgrade.Stat switch
		{
			ShipUpgradeStat.CargoCapacity => CargoLevel,
			ShipUpgradeStat.HullHealth => HullLevel,
			ShipUpgradeStat.SailingPerformance => RiggingLevel,
			_ => 0
		};
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
		switch ( upgrade.Stat )
		{
			case ShipUpgradeStat.CargoCapacity: CargoLevel++; break;
			case ShipUpgradeStat.HullHealth: HullLevel++; break;
			case ShipUpgradeStat.SailingPerformance: RiggingLevel++; break;
		}
		Revision++;
		var health = GetComponent<ShipHealth>();
		var oldMaximum = health.IsValid() ? health.MaximumHealth : 0;
		OnUpdate();
		if ( health.IsValid() && health.MaximumHealth > oldMaximum ) health.Repair( health.MaximumHealth - oldMaximum );
		return TransactionResult.Accepted( level + 1, cost );
	}
}
