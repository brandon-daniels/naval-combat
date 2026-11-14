using System;

namespace NavalCombat;

public sealed class ProductionSite : Component
{
	[Property] public string SiteId { get; set; } = "production.unassigned";
	[Property] public GoodsDefinition OutputGoods { get; set; }
	[Property] public int InvestmentCost { get; set; } = 100;
	[Property] public int OutputQuantity { get; set; } = 10;
	[Property] public float ProductionSeconds { get; set; } = 60;
	[Property, Sync( SyncFlags.FromHost )] public VoyagePort SourcePort { get; set; }
	[Sync( SyncFlags.FromHost )] public float StartedAt { get; private set; }
	public float Progress => IsProducing ? Math.Clamp( (Time.Now - StartedAt) / Math.Max( 0.1f, CompletesAt - StartedAt ), 0, 1 ) : 0;
	[Sync( SyncFlags.FromHost )] public string InvestorId { get; private set; }
	[Sync( SyncFlags.FromHost )] public float CompletesAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool HasOutput { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	public bool IsProducing => !string.IsNullOrEmpty( InvestorId ) && !HasOutput;
	public float RemainingSeconds => HasOutput ? 0 : Math.Max( 0, CompletesAt - Time.Now );

	protected override void OnUpdate()
	{
		if ( GameplayAuthority.CanMutate && IsProducing && Time.Now >= CompletesAt )
		{
			var port = SourcePort.IsValid() ? SourcePort : Scene.GetAllComponents<VoyagePort>().FirstOrDefault( x => x.ProducesGoods );
			var position = port.IsValid() ? port.BarrelPosition : WorldPosition + Vector3.Up * 110;
			var barrel = GoodsBarrel.Spawn( this, position );
			if ( !barrel.IsValid() ) return;
			if ( port.IsValid() && port.BarrelSpawn.IsValid() ) barrel.WorldRotation = port.BarrelSpawn.WorldRotation;
			InvestorId = null;
			CompletesAt = 0;
			HasOutput = false;
			Revision++;
		}
	}

	public TransactionResult Invest( string investorId, PlayerWallet wallet )
	{
		if ( !GameplayAuthority.CanMutate ) return TransactionResult.Rejected( TransactionFailure.NotAuthoritative );
		if ( string.IsNullOrWhiteSpace( investorId ) || !wallet.IsValid() || OutputGoods is null || OutputQuantity <= 0 ) return TransactionResult.Rejected( TransactionFailure.InvalidRequest );
		if ( !string.IsNullOrEmpty( InvestorId ) ) return TransactionResult.Rejected( TransactionFailure.ProductionBusy );
		if ( SourcePort.IsValid() && Scene.GetAllComponents<ProductionSite>().Any( x => x != this && x.SourcePort == SourcePort && x.IsProducing ) )
			return TransactionResult.Rejected( TransactionFailure.ProductionBusy );
		var cost = Math.Max( 0, InvestmentCost );
		if ( !wallet.CanAfford( cost ) ) return TransactionResult.Rejected( TransactionFailure.InsufficientFunds );

		wallet.TryDebit( cost );
		InvestorId = investorId.Trim();
		StartedAt = Time.Now;
		CompletesAt = StartedAt + Math.Max( 0.1f, ProductionSeconds );
		HasOutput = false;
		Revision++;
		return TransactionResult.Accepted( OutputQuantity, cost );
	}

	public TransactionResult Claim( string investorId, CargoHold cargo )
	{
		if ( !GameplayAuthority.CanMutate ) return TransactionResult.Rejected( TransactionFailure.NotAuthoritative );
		if ( investorId != InvestorId ) return TransactionResult.Rejected( TransactionFailure.NotOwner );
		if ( !HasOutput ) return TransactionResult.Rejected( TransactionFailure.ProductionNotReady );
		if ( !cargo.IsValid() || !cargo.CanAdd( OutputGoods, OutputQuantity ) ) return TransactionResult.Rejected( TransactionFailure.InsufficientCapacity );

		cargo.TryAdd( OutputGoods, OutputQuantity );
		var claimed = OutputQuantity;
		InvestorId = null;
		CompletesAt = 0;
		HasOutput = false;
		Revision++;
		return TransactionResult.Accepted( claimed );
	}
}
