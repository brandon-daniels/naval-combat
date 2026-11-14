using System;

namespace NavalCombat;

/// <summary>Owned player's host-validated invest, load, sell and upgrade request boundary.</summary>
public sealed class PlayerVoyage : Component
{
	[Property] public ShipPlayer Sailor { get; set; }
	[Property] public PlayerWallet Wallet { get; set; }
	[Property] public ProductionSite Production { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public string PlayerId { get; set; }
	[Sync( SyncFlags.FromHost )] public string Feedback { get; private set; } = "Sail to the gold buoy at Beacon Island.";
	public CargoHold Cargo => Sailor.Ship.GetComponent<CargoHold>();
	public ShipUpgradeManager Upgrades => Sailor.Ship.GetComponent<ShipUpgradeManager>();
	public VoyagePort NearestPort => Scene.GetAllComponents<VoyagePort>()
		.OrderBy( x => x.WorldPosition.Distance( Sailor.Ship.WorldPosition ) ).FirstOrDefault();
	public string Identity => PlayerId;
	private float nextRequest;
	public bool InvestmentOpen { get; private set; }
	private VoyagePort dialogPort;
	private bool savedInput;
	private bool savedLook;
	public bool CanOpenInvestment => !IsProxy && Sailor.IsValid() && !Sailor.InSwordCombat && Sailor.Ship.IsValid()
		&& !Sailor.CurrentStation.IsValid() && !Sailor.CarriedBarrel.IsValid()
		&& NearestPort is { ProducesGoods: true } port && port.CanTrade( Sailor );

	public void OpenInvestment()
	{
		if ( InvestmentOpen || !CanOpenInvestment || !Sailor.Controller.IsValid() ) return;
		dialogPort = NearestPort;
		InvestmentOpen = true;
		savedInput = Sailor.Controller.UseInputControls;
		savedLook = Sailor.Controller.UseLookControls;
		Sailor.Controller.UseInputControls = false;
		Sailor.Controller.UseLookControls = false;
		Sailor.Controller.WishVelocity = Vector3.Zero;
	}

	public void CloseInvestment()
	{
		if ( !InvestmentOpen ) return;
		InvestmentOpen = false;
		if ( Sailor.IsValid() && Sailor.Controller.IsValid() )
		{
			Sailor.Controller.UseInputControls = savedInput;
			Sailor.Controller.UseLookControls = savedLook;
		}
		dialogPort = null;
	}

	public void ConfirmInvestment()
	{
		if ( !InvestmentOpen ) return;
		bool valid = dialogPort.IsValid() && dialogPort == NearestPort && dialogPort.CanTrade( Sailor );
		CloseInvestment();
		if ( valid ) RequestAction( 1 );
	}

	protected override void OnDisabled() => CloseInvestment();

	protected override void OnUpdate()
	{
		if ( IsProxy || !Sailor.IsValid() || !Sailor.Ship.IsValid() ) return;
		if ( Sailor.InSwordCombat )
		{
			if ( InvestmentOpen ) CloseInvestment();
			return;
		}
		if ( InvestmentOpen )
		{
			if ( !dialogPort.IsValid() || !dialogPort.CanTrade( Sailor ) ) CloseInvestment();
			return;
		}
		// Slots 1–3 are the player's held equipment; port transactions use slots 4–7.
		for ( int action = 2; action <= 5; action++ )
			if ( Input.Pressed( $"Slot{action + 2}" ) ) RequestAction( action );
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
		if ( !Sailor.IsValid() || Sailor.InSwordCombat ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
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
			if ( !Production.IsProducing ) Production.SourcePort = port;
			result = Production.Invest( Identity, Wallet );
			Feedback = result.Success ? "Production started. Return in 60 seconds to collect the barrel." : Explain( result.Failure );
		}
		else if ( action == 2 )
		{
			var barrels = Scene.GetAllComponents<GoodsBarrel>().Where( x => x.SecuredShip == Sailor.Ship && x.Goods == Production.OutputGoods ).ToArray();
			var quantity = barrels.Sum( x => x.Quantity );
			result = port.Port.Sell( Sailor.Ship.GameObject, Wallet, Cargo, Production.OutputGoods, quantity );
			if ( result.Success ) foreach ( var barrel in barrels ) barrel.RemoveAfterSale();
			Feedback = result.Success ? $"Delivered {result.Amount} goods for {result.TotalPrice}." : Explain( result.Failure );
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
		TransactionFailure.ProductionBusy => "This growing area is busy. Wait for the current batch to finish.",
		TransactionFailure.InsufficientFunds => "Not enough money. Deliver cargo to earn more.",
		TransactionFailure.InsufficientCapacity => "Cargo hold is full. Sell cargo or expand the hold.",
		TransactionFailure.MaximumLevel => "This upgrade is already at maximum level.",
		TransactionFailure.InvalidRequest => "No goods aboard to sell.",
		_ => failure.ToString()
	};
}
