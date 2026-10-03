using System;
using System.Collections.Generic;

namespace NavalCombat;

public sealed class IslandPort : Component
{
	[Property] public string PortId { get; set; } = "port.unassigned";
	[Property] public GameObject InteractionPoint { get; set; }
	[Property] public float InteractionRadius { get; set; } = 350;
	[Property] public IslandMarket Market { get; set; }
	[Property] public List<ProductionSite> ProductionSites { get; set; } = new();

	public Vector3 InteractionPosition => InteractionPoint.IsValid() ? InteractionPoint.WorldPosition : WorldPosition;

	public bool Contains( GameObject actor )
	{
		return actor.IsValid() && actor.WorldPosition.Distance( InteractionPosition ) <= Math.Max( 1, InteractionRadius );
	}

	public TransactionResult Buy( GameObject actor, PlayerWallet wallet, CargoHold cargo, GoodsDefinition goods, int quantity )
	{
		if ( !Contains( actor ) ) return TransactionResult.Rejected( TransactionFailure.OutOfRange );
		if ( !Market.IsValid() ) return TransactionResult.Rejected( TransactionFailure.NoMarketListing );
		return Market.Buy( wallet, cargo, goods, quantity );
	}

	public TransactionResult Sell( GameObject actor, PlayerWallet wallet, CargoHold cargo, GoodsDefinition goods, int quantity )
	{
		if ( !Contains( actor ) ) return TransactionResult.Rejected( TransactionFailure.OutOfRange );
		if ( !Market.IsValid() ) return TransactionResult.Rejected( TransactionFailure.NoMarketListing );
		return Market.Sell( wallet, cargo, goods, quantity );
	}

	public TransactionResult Invest( GameObject actor, ProductionSite site, string investorId, PlayerWallet wallet )
	{
		if ( !Contains( actor ) ) return TransactionResult.Rejected( TransactionFailure.OutOfRange );
		if ( !site.IsValid() || !ProductionSites.Contains( site ) ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		return site.Invest( investorId, wallet );
	}

	public TransactionResult Claim( GameObject actor, ProductionSite site, string investorId, CargoHold cargo )
	{
		if ( !Contains( actor ) ) return TransactionResult.Rejected( TransactionFailure.OutOfRange );
		if ( !site.IsValid() || !ProductionSites.Contains( site ) ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		return site.Claim( investorId, cargo );
	}
}
