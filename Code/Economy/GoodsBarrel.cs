using System;

namespace NavalCombat;

/// <summary>A produced batch of goods which must be carried aboard before it can be sold.</summary>
public sealed class GoodsBarrel : Component
{
	public const string ModelPath = "lowpoly pirates/models/barrel.vmdl";
	public const float InteractionDistance = 130;

	[Sync( SyncFlags.FromHost )] public string OwnerId { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Quantity { get; private set; }
	[Sync( SyncFlags.FromHost )] public ShipPlayer Carrier { get; private set; }
	[Sync( SyncFlags.FromHost )] public ArcadeShip SecuredShip { get; private set; }
	public GoodsDefinition Goods { get; private set; }
	public bool IsLoose => !Carrier.IsValid() && !SecuredShip.IsValid();

	private Rigidbody body;
	private ModelCollider collider;
	private Vector3 securedLocalPosition;

	public static GoodsBarrel Spawn( ProductionSite source, Vector3 position )
	{
		if ( !GameplayAuthority.CanMutate || !source.IsValid() || source.OutputGoods is null ) return null;

		var gameObject = new GameObject( source.Scene, true, "Produced goods barrel" );
		gameObject.NetworkMode = Networking.IsActive ? NetworkMode.Object : NetworkMode.Never;
		gameObject.WorldPosition = position;
		var barrel = gameObject.AddComponent<GoodsBarrel>();
		barrel.OwnerId = source.InvestorId;
		barrel.Quantity = source.OutputQuantity;
		barrel.Goods = source.OutputGoods;
		barrel.BuildPhysicalBarrel();
		// Keep finished production staged at the offshore buoy instead of letting it
		// fall through the non-physical ocean before its owner returns.
		barrel.SetPhysicsEnabled( false );
		if ( Networking.IsActive ) gameObject.NetworkSpawn( null );
		return barrel;
	}

	protected override void OnStart()
	{
		if ( !GetComponent<ModelRenderer>().IsValid() ) BuildPhysicalBarrel();
		ResolveGoods();
	}

	private void BuildPhysicalBarrel()
	{
		var model = Model.Load( ModelPath );
		var renderer = GetComponent<ModelRenderer>() ?? GameObject.AddComponent<ModelRenderer>();
		renderer.Model = model;
		collider = GetComponent<ModelCollider>() ?? GameObject.AddComponent<ModelCollider>();
		collider.Model = model;
		body = GetComponent<Rigidbody>() ?? GameObject.AddComponent<Rigidbody>();
		body.MassOverride = 20;
	}

	private void ResolveGoods()
	{
		if ( Goods is not null ) return;
		Goods = Scene.GetAllComponents<NavalGameSession>().FirstOrDefault()?.FindGoods( "goods.trade_goods" )
			?? ResourceLibrary.Get<GoodsDefinition>( "definitions/goods/trade_goods.ngoods" );
	}

	public bool CanPickup( ShipPlayer player )
	{
		return IsLoose && player.IsValid() && !player.CarriedBarrel.IsValid()
			&& player.IdentityMatches( OwnerId ) && player.WorldPosition.Distance( WorldPosition ) <= InteractionDistance;
	}

	public bool TryPickup( ShipPlayer player )
	{
		if ( !GameplayAuthority.CanMutate || !CanPickup( player ) ) return false;
		Carrier = player;
		player.SetCarriedBarrel( this );
		SetPhysicsEnabled( false );
		return true;
	}

	public bool DropOrSecure( ShipPlayer player )
	{
		if ( !GameplayAuthority.CanMutate || Carrier != player ) return false;
		Carrier = null;
		player.SetCarriedBarrel( null );

		var cargo = player.Ship.GetComponent<CargoHold>();
		if ( player.WorldPosition.Distance( player.Ship.WorldPosition ) <= 450 && cargo.IsValid() && cargo.TryAdd( Goods, Quantity ) )
		{
			SecuredShip = player.Ship;
			var index = Scene.GetAllComponents<GoodsBarrel>().Count( x => x != this && x.SecuredShip == SecuredShip );
			securedLocalPosition = new Vector3( -60 - (index % 3) * 55, (index / 3 - 1) * 55, 105 );
			SetPhysicsEnabled( false );
			UpdateSecuredTransform();
			return true;
		}

		SetPhysicsEnabled( true );
		return true;
	}

	public void RemoveAfterSale()
	{
		if ( !GameplayAuthority.CanMutate ) return;
		GameObject.Destroy();
	}

	protected override void OnUpdate()
	{
		if ( !GameplayAuthority.CanMutate ) return;
		if ( Carrier.IsValid() )
		{
			var forward = Carrier.Controller.IsValid() ? Carrier.Controller.EyeAngles.ToRotation().Forward : Carrier.WorldRotation.Forward;
			WorldPosition = Carrier.WorldPosition + Vector3.Up * 50 + forward * 55;
			WorldRotation = Rotation.FromYaw( Carrier.WorldRotation.Angles().yaw + 90 );
		}
		else if ( SecuredShip.IsValid() )
		{
			UpdateSecuredTransform();
		}
	}

	private void UpdateSecuredTransform()
	{
		WorldPosition = SecuredShip.WorldTransform.PointToWorld( securedLocalPosition );
		WorldRotation = SecuredShip.WorldRotation;
	}

	private void SetPhysicsEnabled( bool enabled )
	{
		body ??= GetComponent<Rigidbody>();
		collider ??= GetComponent<ModelCollider>();
		if ( body.IsValid() ) body.Enabled = enabled;
		if ( collider.IsValid() ) collider.Enabled = enabled;
	}
}
