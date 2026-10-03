using System;

namespace NavalCombat;

public enum ShipUpgradeStat
{
	CargoCapacity,
	HullHealth,
	SailingPerformance
}

[AssetType( Name = "Ship Upgrade", Extension = "navalupgrade", Category = "Naval Combat" )]
public sealed class ShipUpgradeDefinition : GameResource
{
	[Property] public string Id { get; set; } = "upgrade.unassigned";
	[Property] public string DisplayName { get; set; } = "Ship Upgrade";
	[Property] public ShipUpgradeStat Stat { get; set; }
	[Property] public int MaximumLevel { get; set; } = 3;
	[Property] public int BaseCost { get; set; } = 250;
	[Property] public float CostMultiplier { get; set; } = 1.75f;
	[Property] public float AmountPerLevel { get; set; } = 10;

	public string StableId => string.IsNullOrWhiteSpace( Id ) ? ResourceName : Id.Trim();

	public int GetCost( int currentLevel )
	{
		return Math.Max( 0, (int)MathF.Round( BaseCost * MathF.Pow( Math.Max( 1, CostMultiplier ), Math.Max( 0, currentLevel ) ) ) );
	}
}
