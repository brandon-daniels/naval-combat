using System;
using System.Collections.Generic;
using System.Linq;

namespace NavalCombat;

/// <summary>Scene-level registry for the integrated game loop.</summary>
public sealed class NavalGameSession : Component
{
	[Property] public List<IslandPort> Ports { get; set; } = new();
	[Property] public List<GoodsDefinition> GoodsCatalog { get; set; } = new();
	[Property] public float SessionMinutes { get; set; } = 30;
	[Property] public int StartingMoney { get; set; } = 500;
	[Property] public float DefeatedCargoDropFraction { get; set; } = 0.75f;
	[Property] public int RespawnCost { get; set; }
	[Property] public bool UpgradesPersistBetweenSessions { get; set; }

	public bool IsAuthority => GameplayAuthority.CanMutate;

	protected override void OnStart()
	{
		if ( !IsAuthority ) return;
		SessionMinutes = Math.Max( 1, SessionMinutes );
		StartingMoney = Math.Max( 0, StartingMoney );
		DefeatedCargoDropFraction = Math.Clamp( DefeatedCargoDropFraction, 0, 1 );
		RespawnCost = Math.Max( 0, RespawnCost );
	}

	public IslandPort FindPort( string portId )
	{
		if ( string.IsNullOrWhiteSpace( portId ) ) return null;
		return Ports.FirstOrDefault( x => x.IsValid() && string.Equals( x.PortId, portId, StringComparison.OrdinalIgnoreCase ) );
	}

	public GoodsDefinition FindGoods( string goodsId )
	{
		if ( string.IsNullOrWhiteSpace( goodsId ) ) return null;
		return GoodsCatalog.FirstOrDefault( x => x is not null && string.Equals( x.StableId, goodsId, StringComparison.OrdinalIgnoreCase ) );
	}
}
