using System;
using System.Collections.Generic;
using System.Linq;

namespace NavalCombat;

public sealed class IslandMarket : Component
{
	[Property] public List<MarketListing> Listings { get; set; } = new();

	public bool TryGetUnitPrice( GoodsDefinition goods, bool sellingToMarket, out int unitPrice )
	{
		unitPrice = 0;
		if ( goods is null ) return false;
		var listing = Listings.FirstOrDefault( x => x.Goods == goods );
		if ( listing is null ) return false;
		unitPrice = Math.Max( 0, sellingToMarket ? listing.SellPrice : listing.BuyPrice );
		return unitPrice > 0;
	}

	public TransactionResult Sell( PlayerWallet wallet, CargoHold cargo, GoodsDefinition goods, int quantity )
	{
		if ( !GameplayAuthority.CanMutate ) return TransactionResult.Rejected( TransactionFailure.NotAuthoritative );
		if ( !wallet.IsValid() || !cargo.IsValid() || goods is null || quantity <= 0 ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		if ( !TryGetUnitPrice( goods, true, out var unitPrice ) ) return TransactionResult.Rejected( TransactionFailure.NoMarketListing );
		if ( !cargo.CanRemove( goods, quantity ) ) return TransactionResult.Rejected( TransactionFailure.InsufficientGoods );
		long total = (long)unitPrice * quantity;
		if ( total > int.MaxValue || !wallet.CanCredit( (int)total ) ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );

		cargo.TryRemove( goods, quantity );
		wallet.TryCredit( (int)total );
		return TransactionResult.Accepted( quantity, (int)total );
	}

	public TransactionResult Buy( PlayerWallet wallet, CargoHold cargo, GoodsDefinition goods, int quantity )
	{
		if ( !GameplayAuthority.CanMutate ) return TransactionResult.Rejected( TransactionFailure.NotAuthoritative );
		if ( !wallet.IsValid() || !cargo.IsValid() || goods is null || quantity <= 0 ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		if ( !TryGetUnitPrice( goods, false, out var unitPrice ) ) return TransactionResult.Rejected( TransactionFailure.NoMarketListing );
		long total = (long)unitPrice * quantity;
		if ( total > int.MaxValue || !wallet.CanAfford( (int)total ) ) return TransactionResult.Rejected( TransactionFailure.InsufficientFunds );
		if ( !cargo.CanAdd( goods, quantity ) ) return TransactionResult.Rejected( TransactionFailure.InsufficientCapacity );

		wallet.TryDebit( (int)total );
		cargo.TryAdd( goods, quantity );
		return TransactionResult.Accepted( quantity, (int)total );
	}
}

public sealed class MarketListing
{
	[Property] public GoodsDefinition Goods { get; set; }
	[Property] public int BuyPrice { get; set; } = 10;
	[Property] public int SellPrice { get; set; } = 12;
}
