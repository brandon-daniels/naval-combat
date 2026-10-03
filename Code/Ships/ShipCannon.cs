using System;
namespace NavalCombat;

public sealed class ShipCannon : ShipStation
{
	[Property] public GameObject Barrel { get; set; }
	[Property] public GameObject Muzzle { get; set; }
	[Property] public float ReloadSeconds { get; set; } = 2;
	[Property] public float MuzzleSpeed { get; set; } = 1800;
	public float Yaw { get; private set; }
	public float Elevation { get; private set; } = 12;
	public float ReloadRemaining => Math.Max( 0, readyAt - Time.Now );
	private float readyAt;
	private float recoil;
	public override Transform CalculateEyeTransform( PlayerController player )
	{
		if ( !EyePosition.IsValid() || !Barrel.IsValid() ) return base.CalculateEyeTransform( player );
		return new Transform( EyePosition.WorldPosition, Barrel.WorldRotation );
	}
	public void Aim( PlayerController player, float turn, float lift, float delta )
	{
		if ( GetOccupant() != player || !player.IsValid() || !Enabled ) return;
		Yaw = Math.Clamp( Yaw + turn * 35 * Math.Clamp( delta, 0, 0.1f ), -35, 35 );
		Elevation = Math.Clamp( Elevation + lift * 25 * Math.Clamp( delta, 0, 0.1f ), 0, 45 );
		UpdateBarrel();
	}
	public bool Fire( PlayerController player )
	{
		if ( Networking.IsActive || !Enabled || !player.IsValid() || GetOccupant() != player || ReloadRemaining > 0 || !Muzzle.IsValid() || !Ship.IsValid() ) return false;
		UpdateBarrel();
		var ball = new GameObject( Scene, true, "Cannonball" );
		ball.NetworkMode = NetworkMode.Never;
		ball.WorldPosition = Muzzle.WorldPosition;
		var shot = ball.AddComponent<Cannonball>();
		shot.Source = Ship;
		shot.Velocity = Muzzle.WorldRotation.Forward * MuzzleSpeed + Ship.Body.GetVelocityAtPoint( Muzzle.WorldPosition );
		CannonBurst.Spawn( Scene, Muzzle.WorldPosition, Muzzle.WorldRotation.Forward, false );
		readyAt = Time.Now + Math.Max( 0.2f, ReloadSeconds );
		recoil = 1;
		return true;
	}
	protected override void OnUpdate()
	{
		recoil = Math.Max( 0, recoil - Time.Delta * 3 );
		UpdateBarrel();
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
	private float age;
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
		age += Time.Delta;
		if ( age > 8 || !Source.IsValid() ) { GameObject.Destroy(); return; }
		var start = WorldPosition;
		Velocity += Vector3.Down * 400 * Time.Delta;
		var end = start + Velocity * Time.Delta;
		var hit = Scene.Trace.Ray( start, end ).Radius( 7 ).IgnoreGameObjectHierarchy( Source.GameObject ).IgnoreGameObjectHierarchy( GameObject ).Run();
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
			CannonBurst.Spawn( Scene, WorldPosition, water ? Vector3.Up : hit.Normal, water );
			GameObject.Destroy();
		}
	}
}

public sealed class CannonBurst : Component
{
	private float age;
	private Vector3 velocity;
	private bool water;
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
		float life = water ? 0.65f : 0.28f;
		if ( age >= life ) { GameObject.Destroy(); return; }
		WorldPosition += velocity * Time.Delta;
		velocity += Vector3.Down * 120 * Time.Delta;
		WorldScale = Vector3.One * (0.3f * (1 - age / life));
	}
}
