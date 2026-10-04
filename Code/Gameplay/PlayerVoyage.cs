using System;

namespace NavalCombat;

/// <summary>Owned player's host-validated invest, load, sell and upgrade request boundary.</summary>
public sealed class PlayerVoyage : Component
{
	[Property] public ShipPlayer Sailor { get; set; }
	[Property] public PlayerWallet Wallet { get; set; }
	[Property] public ProductionSite Production { get; set; }
	[Property] public string PlayerId { get; set; }
	[Sync( SyncFlags.FromHost )] public string Feedback { get; private set; } = "Sail to the gold buoy at Beacon Island.";
	public CargoHold Cargo => Sailor.Ship.GetComponent<CargoHold>();
	public ShipUpgradeManager Upgrades => Sailor.Ship.GetComponent<ShipUpgradeManager>();
	public VoyagePort NearestPort => Scene.GetAllComponents<VoyagePort>()
		.OrderBy( x => x.WorldPosition.Distance( Sailor.Ship.WorldPosition ) ).FirstOrDefault();
	public string Identity => PlayerId;
	private float nextRequest;

	protected override void OnUpdate()
	{
		if ( IsProxy || !Sailor.IsValid() || !Sailor.Ship.IsValid() ) return;
		for ( int i = 1; i <= 5; i++ )
			if ( Input.Pressed( $"Slot{i}" ) ) RequestAction( i );
	}

	public void RequestAction( int action )
	{
		if ( Networking.IsActive ) RequestFromOwner( action );
		else Execute( action );
	}

	[Rpc.Host]
	private void RequestFromOwner( int action )
	{
		if ( Rpc.Caller != Network.Owner || !Sailor.IsValid() || Sailor.Ship.Network.Owner != Rpc.Caller ) return;
		Execute( action );
	}

	public TransactionResult Execute( int action )
	{
		if ( !GameplayAuthority.CanMutate ) return TransactionResult.Rejected( TransactionFailure.NotAuthoritative );
		if ( Time.Now < nextRequest ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		nextRequest = Time.Now + 0.2f;
		var port = NearestPort;
		if ( !port.IsValid() || !port.CanTrade( Sailor ) )
		{
			Feedback = "Move within 650 units of a buoy and raise the sail to slow below 100.";
			return TransactionResult.Rejected( TransactionFailure.OutOfRange );
		}

		TransactionResult result;
		if ( action == 1 && port.ProducesGoods )
		{
			result = Production.HasOutput ? Production.Claim( Identity, Cargo ) : Production.Invest( Identity, Wallet );
			Feedback = result.Success ? (Production.HasOutput || Production.IsProducing ? "Production started. Return in 60 seconds to load." : "Loaded 10 Trade Goods. Sell at Palm Island for 350.") : Explain( result.Failure );
		}
		else if ( action == 2 )
		{
			result = port.Port.Sell( Sailor.Ship.GameObject, Wallet, Cargo, Production.OutputGoods, Cargo.GetQuantity( Production.OutputGoods ) );
			Feedback = result.Success ? $"Sold {result.Amount} goods for {result.TotalPrice}." : Explain( result.Failure );
		}
		else if ( action >= 3 && action <= 5 )
		{
			var stat = (ShipUpgradeStat)(action - 3);
			var upgrade = Upgrades.AvailableUpgrades.FirstOrDefault( x => x is not null && x.Stat == stat );
			result = Upgrades.Purchase( Wallet, upgrade );
			Feedback = result.Success ? $"Purchased {upgrade.DisplayName}, level {result.Amount}." : Explain( result.Failure );
		}
		else
		{
			result = TransactionResult.Rejected( TransactionFailure.InvalidRequest );
			Feedback = "Production is available at Beacon Island's gold buoy.";
		}
		return result;
	}

	private string Explain( TransactionFailure failure ) => failure switch
	{
		TransactionFailure.ProductionBusy => $"Production: {MathF.Ceiling( Production.RemainingSeconds )} seconds remaining.",
		TransactionFailure.InsufficientFunds => "Not enough money. Deliver cargo to earn more.",
		TransactionFailure.InsufficientCapacity => "Cargo hold is full. Sell cargo or expand the hold.",
		TransactionFailure.MaximumLevel => "This upgrade is already at maximum level.",
		TransactionFailure.InvalidRequest => "No goods aboard to sell.",
		_ => failure.ToString()
	};
}
