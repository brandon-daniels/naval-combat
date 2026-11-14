using System;
namespace NavalCombat;

public sealed class ShipCannon : ShipStation, Component.ExecuteInEditor
{
	private const string AuthoredCannonModel = "lowpoly pirates/models/cannon.vmdl";
	[Property] public GameObject Barrel { get; set; }
	[Property] public GameObject Muzzle { get; set; }
	[Property] public float ReloadSeconds { get; set; } = 2;
	[Property] public float MuzzleSpeed { get; set; } = 1800;
	[Property] public float MaximumDepression { get; set; } = 10;
	public float MinimumElevation => -Math.Clamp( MaximumDepression, 0, 20 );
	[Sync] public float Yaw { get; private set; }
	[Sync] public float Elevation { get; private set; } = 12;
	[Sync( SyncFlags.FromHost )] public bool IsLoaded { get; private set; }
	[Sync( SyncFlags.FromHost )] public PlayerController Loader { get; private set; }
	[Sync( SyncFlags.FromHost )] public float LoadingStartedAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public float ReadyAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public int ShotSequence { get; private set; }
	internal int PresentedShotSequence => presentedShot;
	public bool IsLoading => Loader.IsValid();
	public float ReloadRemaining => IsLoading ? Math.Max( 0, ReadyAt - Time.Now ) : 0;
	public float LoadProgress => IsLoaded ? 1 : IsLoading
		? Math.Clamp( 1 - ReloadRemaining / Math.Max( 0.1f, ReadyAt - LoadingStartedAt ), 0, 1 ) : 0;
	private const float LoadingLeaseSeconds = 0.6f;
	private float loadingLeaseUntil;
	private bool requestedLoading;
	private float nextLoadingRequest;
	private int presentedShot;
	private float recoil;
	private GameObject authoredVisual;
	private bool prototypeVisualsHidden;

	protected override void OnStart() => ConfigureAuthoredVisual();
	protected override void OnEnabled() => ConfigureAuthoredVisual();
	public override Transform CalculateEyeTransform( PlayerController player )
	{
		if ( !EyePosition.IsValid() || !Barrel.IsValid() ) return base.CalculateEyeTransform( player );
		return new Transform( EyePosition.WorldPosition, Barrel.WorldRotation * Rotation.FromPitch( -recoil * 1.8f ) );
	}
	public void Aim( PlayerController player, float turn, float lift, float delta )
	{
		if ( Occupant != player || !player.IsValid() || !Enabled || !Ship.IsValid() || Ship.IsSinking ) return;
		if ( !float.IsFinite( turn ) || !float.IsFinite( lift ) || !float.IsFinite( delta ) ) return;
		turn = Math.Clamp( turn, -1, 1 );
		lift = Math.Clamp( lift, -1, 1 );
		Yaw = Math.Clamp( Yaw + turn * 35 * Math.Clamp( delta, 0, 0.1f ), -35, 35 );
		Elevation = Math.Clamp( Elevation + lift * 25 * Math.Clamp( delta, 0, 0.1f ), MinimumElevation, 45 );
		UpdateBarrel();
	}

	/// <summary>Call while mounted every input tick. Releasing the key or station discards unfinished loading.</summary>
	public bool SetLoading( PlayerController player, bool held )
	{
		if ( !player.IsValid() || (Networking.IsActive && player.IsProxy) ) return false;
		if ( held && (!CanOperate( player ) || IsLoaded) ) held = false;
		if ( !Networking.IsActive || CanControlNpc( player ) ) return SetLoadingAuthoritative( player, held );
		if ( requestedLoading == held && (!held || Time.Now < nextLoadingRequest) ) return held;
		requestedLoading = held;
		nextLoadingRequest = Time.Now + 0.12f;
		RequestLoading( player, held );
		return held;
	}

	[Rpc.Host]
	internal void RequestLoading( PlayerController player, bool held )
	{
		if ( !player.IsValid() || !Ship.IsValid() || player.Network.Owner != Rpc.Caller || Ship.Network.Owner != Rpc.Caller ) return;
		SetLoadingAuthoritative( player, held );
	}

	private bool SetLoadingAuthoritative( PlayerController player, bool held )
	{
		if ( !GameplayAuthority.CanMutate ) return false;
		if ( !held )
		{
			if ( Loader == player ) CancelLoading();
			return false;
		}
		if ( !CanOperate( player ) || IsLoaded ) return false;
		if ( !IsLoading )
		{
			Loader = player;
			LoadingStartedAt = Time.Now;
			ReadyAt = Time.Now + Math.Clamp( ReloadSeconds, 0.5f, 10 );
		}
		loadingLeaseUntil = Time.Now + LoadingLeaseSeconds;
		return true;
	}

	private bool CanOperate( PlayerController player ) => Enabled && player.IsValid() && player.Enabled
		&& Occupant == player && Ship.IsValid() && !Ship.IsSinking && Muzzle.IsValid() && Ship.Body.IsValid();

	private void CancelLoading()
	{
		Loader = null;
		LoadingStartedAt = 0;
		ReadyAt = 0;
		loadingLeaseUntil = 0;
	}

	protected override void OnFixedUpdate()
	{
		if ( Scene.IsEditor || !GameplayAuthority.CanMutate || ReadyAt <= 0 ) return;
		if ( !CanOperate( Loader ) || Time.Now > loadingLeaseUntil )
		{
			CancelLoading();
			return;
		}
		if ( Time.Now < ReadyAt ) return;
		IsLoaded = true;
		CancelLoading();
	}

	public bool Fire( PlayerController player )
	{
		if ( !CanOperate( player ) || !IsLoaded || (Networking.IsActive && player.IsProxy) ) return false;
		if ( Networking.IsActive )
		{
			if ( CanControlNpc( player ) ) return FireAuthoritative( player );
			RequestFire( player );
			return true;
		}
		return FireAuthoritative( player );
	}

	[Rpc.Host]
	internal void RequestFire( PlayerController player )
	{
		if ( !player.IsValid() || !Ship.IsValid() || player.Network.Owner != Rpc.Caller || Ship.Network.Owner != Rpc.Caller ) return;
		FireAuthoritative( player );
	}

	private bool FireAuthoritative( PlayerController player )
	{
		if ( !GameplayAuthority.CanMutate || !CanOperate( player ) || !IsLoaded ) return false;
		// Aim is owner-simulated, but a shot must still respect the physical gun's limits on the host.
		if ( !float.IsFinite( Yaw ) || !float.IsFinite( Elevation ) || Math.Abs( Yaw ) > 35
			|| Elevation < MinimumElevation || Elevation > 45 || !float.IsFinite( MuzzleSpeed ) ) return false;
		UpdateBarrel();
		var position = Muzzle.WorldPosition;
		var direction = Muzzle.WorldRotation.Forward;
		var inheritedVelocity = Ship.Body.GetVelocityAtPoint( position );
		var ball = new GameObject( Scene, true, "Cannonball" );
		ball.NetworkMode = Networking.IsActive ? NetworkMode.Object : NetworkMode.Never;
		ball.WorldPosition = position;
		var shot = ball.AddComponent<Cannonball>();
		shot.Source = Ship;
		shot.Velocity = direction * Math.Clamp( MuzzleSpeed, 100, 6000 ) + inheritedVelocity;
		IsLoaded = false;
		ShotSequence++;
		if ( Networking.IsActive ) ball.NetworkSpawn( null );
		PresentShot( position, direction, inheritedVelocity, ShotSequence );
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PresentShot( Vector3 position, Vector3 direction, Vector3 inheritedVelocity, int sequence )
	{
		if ( sequence <= presentedShot ) return;
		presentedShot = sequence;
		recoil = 1;
		CannonBurst.SpawnMuzzle( Scene, position, direction, inheritedVelocity );
	}

	protected override void OnUpdate()
	{
		ConfigureAuthoredVisual();
		recoil = Math.Max( 0, recoil - Time.Delta * 3 );
		UpdateBarrel();
	}

	protected override void OnDisabled()
	{
		if ( GameplayAuthority.CanMutate ) CancelLoading();
		requestedLoading = false;
		base.OnDisabled();
	}

	private void ConfigureAuthoredVisual()
	{
		if ( !Ship.IsValid() ) return;
		if ( !authoredVisual.IsValid() )
		{
			authoredVisual = GameObject.GetAllObjects( true )
				.FirstOrDefault( child => child.Name == "Lowpoly Pirates cannon visual" );
		}
		if ( !authoredVisual.IsValid() )
		{
			var model = Model.Load( AuthoredCannonModel );
			if ( model is null ) return;
			authoredVisual = new GameObject( GameObject, true, "Lowpoly Pirates cannon visual" );
			authoredVisual.NetworkMode = NetworkMode.Never;
			authoredVisual.Flags |= GameObjectFlags.NotSaved;
			var renderer = authoredVisual.AddComponent<ModelRenderer>();
			renderer.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
			renderer.Model = model;
		}

		authoredVisual.LocalPosition = new Vector3( -recoil * 12, 0, 0 );
		authoredVisual.LocalRotation = Rotation.FromYaw( Yaw ) * Rotation.FromPitch( -Elevation );
		// Imported model is already 173 units long; fit it to a 130-unit deck gun.
		authoredVisual.LocalScale = Vector3.One * 0.75f;
		if ( prototypeVisualsHidden ) return;
		foreach ( var renderer in Scene.GetAllComponents<ModelRenderer>().Where( renderer =>
			GameObject.IsDescendant( renderer.GameObject ) &&
			!authoredVisual.IsDescendant( renderer.GameObject ) && renderer.GameObject != authoredVisual ) )
		{
			renderer.Enabled = false;
		}
		prototypeVisualsHidden = true;
	}
	private void UpdateBarrel()
	{
		if ( !Barrel.IsValid() ) return;
		Barrel.LocalRotation = Rotation.FromYaw( Yaw ) * Rotation.FromPitch( -Elevation );
		Barrel.LocalPosition = new Vector3( -recoil * 12, 0, 65 );
	}
}

public sealed class Cannonball : Component
{
	public ArcadeShip Source { get; set; }
	public Vector3 Velocity { get; set; }
	[Property] public float MassKilograms { get; set; } = 5.4f;
	private float age;
	private GameObject previousSurface;
	private float surfaceClearTime;
	protected override void OnStart()
	{
		var visual = new GameObject( GameObject, true, "Iron ball" );
		visual.LocalScale = Vector3.One * 0.28f;
		var renderer = visual.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
		renderer.Tint = new Color( 0.08f, 0.09f, 0.1f );
	}
	protected override void OnFixedUpdate()
	{
		if ( !GameplayAuthority.CanMutate || IsProxy ) return;
		age += Time.Delta;
		if ( age > 8 || !Source.IsValid() ) { GameObject.Destroy(); return; }
		var start = WorldPosition;
		Velocity += Vector3.Down * 400 * Time.Delta;
		var end = start + Velocity * Time.Delta;
		var trace = Scene.Trace.Ray( start, end ).Radius( 7 ).WithoutTags( ShipDebris.Tag ).IgnoreGameObjectHierarchy( Source.GameObject ).IgnoreGameObjectHierarchy( GameObject );
		if ( previousSurface.IsValid() && age < surfaceClearTime ) trace = trace.IgnoreGameObjectHierarchy( previousSurface );
		var hit = trace.Run();
		float fraction = hit.Hit ? hit.Fraction : 1;
		bool water = false;
		if ( Source.Ocean.IsValid() )
		{
			float before = start.z - Source.Ocean.HeightAt( start, Time.Now );
			float after = end.z - Source.Ocean.HeightAt( end, Time.Now );
			if ( after <= 0 )
			{
				float crossing = Math.Clamp( before / Math.Max( 0.001f, before - after ), 0, 1 );
				if ( crossing <= fraction ) { fraction = crossing; water = true; }
			}
		}
		WorldPosition = start + (end - start) * fraction;
		if ( hit.Hit || water )
		{
			bool shipImpact = false;
			GameObject struckSurface = null;
			if ( !water && hit.Hit && hit.GameObject.IsValid() )
			{
				var target = Scene.GetAllComponents<ArcadeShip>().FirstOrDefault( ship =>
					ship != Source && (ship.GameObject == hit.GameObject || ship.GameObject.IsDescendant( hit.GameObject )) );
				shipImpact = target.IsValid();
				if ( target.IsValid() )
				{
					// Compound rigidbodies report the ship root as GameObject. Classify the actual shape.
					var surface = hit.Collider.IsValid() ? hit.Collider.GameObject : hit.GameObject;
					struckSurface = surface;
					var part = surface.GetComponent<ShipStructurePart>();
					if ( part.IsValid() && part.IsPlank )
					{
						target.GetComponent<ShipHealth>()?.ApplyCannonHit( hit.HitPosition, hit.Normal, Velocity );
					}
					else if ( part.IsValid() ) target.GetComponent<ShipHealth>()?.BreakPart( part.Index, Velocity );
					else if ( surface.Tags.Has( "ship_hull" ) )
						target.GetComponent<ShipHealth>()?.ApplyCannonHit( hit.HitPosition, hit.Normal, Velocity );
				}
			}
			// Solid shot tears wood; it does not explode into an orange fireball.
			if ( !shipImpact ) CannonBurst.Spawn( Scene, WorldPosition, water ? Vector3.Up : hit.Normal, water );
			if ( shipImpact && !water && Velocity.Length > 450 )
			{
				// Work spent fracturing timber reduces kinetic energy, rather than deleting momentum.
				float speed = Velocity.Length;
				float energy = 0.5f * Math.Max( 0.1f, MassKilograms ) * speed * speed;
				float remaining = Math.Max( 0, energy - 650000 );
				Velocity = Velocity.Normal * MathF.Sqrt( 2 * remaining / Math.Max( 0.1f, MassKilograms ) );
				previousSurface = struckSurface;
				surfaceClearTime = age + 0.12f;
				WorldPosition += Velocity.Normal * 16;
				if ( Velocity.Length > 200 ) return;
			}
			GameObject.Destroy();
		}
	}
}

public sealed class CannonBurst : Component
{
	private float age;
	private Vector3 velocity;
	private bool water;
	private bool smoke;
	private ModelRenderer smokeRenderer;
	public static void SpawnMuzzle( Scene scene, Vector3 position, Vector3 direction, Vector3 inheritedVelocity )
	{
		Spawn( scene, position, direction, false );
		for ( int i = 0; i < 8; i++ )
		{
			var go = new GameObject( scene, true, "Cannon smoke" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved;
			go.WorldPosition = position + direction * i * 4;
			go.WorldScale = Vector3.One * 0.2f;
			var puff = go.AddComponent<CannonBurst>();
			puff.smoke = true;
			puff.velocity = inheritedVelocity + direction * (65 + i * 13)
				+ new Vector3( MathF.Sin( i * 2.4f ), MathF.Cos( i * 2.4f ), 0.6f ) * 35;
			puff.smokeRenderer = go.AddComponent<ModelRenderer>();
			puff.smokeRenderer.Model = Model.Load( "models/dev/sphere.vmdl" );
			puff.smokeRenderer.Tint = new Color( 0.66f, 0.64f, 0.59f, 0.65f );
		}
	}

	public static void Spawn( Scene scene, Vector3 position, Vector3 direction, bool splash )
	{
		for ( int i = 0; i < 6; i++ )
		{
			var go = new GameObject( scene, true, splash ? "Cannon splash" : "Cannon flash" );
			go.NetworkMode = NetworkMode.Never;
			go.WorldPosition = position;
			var burst = go.AddComponent<CannonBurst>();
			burst.water = splash;
			burst.velocity = direction * (60 + i * 20) + new Vector3( MathF.Sin( i * 2.4f ), MathF.Cos( i * 2.4f ), 0 ) * 60;
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
			renderer.Tint = splash ? new Color( 0.8f, 0.95f, 1 ) : new Color( 1, 0.65f, 0.18f );
			go.WorldScale = Vector3.One * 0.3f;
		}
	}
	protected override void OnUpdate()
	{
		age += Time.Delta;
		float life = smoke ? 1.6f : water ? 0.65f : 0.18f;
		if ( age >= life ) { GameObject.Destroy(); return; }
		WorldPosition += velocity * Time.Delta;
		if ( smoke )
		{
			velocity += Vector3.Up * 18 * Time.Delta;
			velocity *= Math.Max( 0, 1 - Time.Delta * 1.2f );
			WorldScale = Vector3.One * (0.2f + age * 0.75f);
			if ( smokeRenderer.IsValid() ) smokeRenderer.Tint = new Color( 0.66f, 0.64f, 0.59f, 0.65f * (1 - age / life) );
			return;
		}
		velocity += Vector3.Down * 120 * Time.Delta;
		WorldScale = Vector3.One * (0.3f * (1 - age / life));
	}
}
