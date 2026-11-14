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
	private CapsuleCollider collider;
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
		barrel.SetPhysicsEnabled( true );
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
		// The imported barrel is render-only. Give it a real rounded collision shape.
		collider = GetComponent<CapsuleCollider>() ?? GameObject.AddComponent<CapsuleCollider>();
		var bounds = model.Bounds;
		collider.Radius = Math.Max( 1, Math.Min( bounds.Size.x, bounds.Size.y ) * 0.5f );
		float halfSegment = Math.Max( 0, bounds.Size.z * 0.5f - collider.Radius );
		collider.Start = bounds.Center - Vector3.Up * halfSegment;
		collider.End = bounds.Center + Vector3.Up * halfSegment;
		collider.Friction = 0.6f;
		collider.RollingResistance = 0.1f;
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

	protected override void OnFixedUpdate()
	{
		if ( !GameplayAuthority.CanMutate || !IsLoose ) return;
		body ??= GetComponent<Rigidbody>();
		var ocean = Scene.GetAllComponents<ArcadeOcean>().FirstOrDefault();
		if ( !body.IsValid() || !body.Enabled || !ocean.IsValid() ) return;
		if ( Scene.GetAllComponents<ArcadeShip>().Any( x => x.ContainsCargoInterior( WorldPosition ) ) ) return;
		float depth = ocean.HeightAt( WorldPosition, Time.Now ) + 12 - WorldPosition.z;
		if ( depth <= 0 ) return;
		// The ocean has no collider. Buoyancy keeps loose cargo physical and recoverable.
		body.ApplyForce( Vector3.Up * body.MassOverride * Math.Clamp( 800 + depth * 35 - body.Velocity.z * 12, 0, 2400 ) );
	}

	private void SetPhysicsEnabled( bool enabled )
	{
		body ??= GetComponent<Rigidbody>();
		collider ??= GetComponent<CapsuleCollider>();
		if ( body.IsValid() ) body.Enabled = enabled;
		if ( collider.IsValid() ) collider.Enabled = enabled;
	}
}
