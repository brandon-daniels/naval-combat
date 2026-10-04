using System;

namespace NavalCombat;

[AssetType( Name = "Naval Goods", Extension = "ngoods", Category = "Naval Combat" )]
public sealed class GoodsDefinition : GameResource
{
	[Property] public string Id { get; set; } = "goods.cargo";
	[Property] public string DisplayName { get; set; } = "Cargo";
	[Property] public int UnitSize { get; set; } = 1;
	[Property] public int BaseValue { get; set; } = 10;

	public string StableId => string.IsNullOrWhiteSpace( Id ) ? ResourceName : Id.Trim();
	public int SafeUnitSize => Math.Max( 1, UnitSize );
}
