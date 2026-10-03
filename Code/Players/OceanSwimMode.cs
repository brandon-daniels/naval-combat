using System;
using Sandbox.Movement;

namespace NavalCombat;

/// <summary>Swimming in the same wave field used by ship buoyancy and the ocean shader.</summary>
public sealed class OceanSwimMode : MoveMode
{
	[Property] public ArcadeOcean Ocean { get; set; }
	[Property] public float SwimSpeed { get; set; } = 190;
	[Property] public float FloatDepth { get; set; } = 42;
	[Property] public float WaterJumpSpeed { get; set; } = 540;
	private TimeUntil jumpLockout;
	private bool dive;

	public override int Score( PlayerController controller )
	{
		if ( !Ocean.IsValid() || jumpLockout > 0 || controller.IsOnGround ) return -100;
		var sailor = GetComponent<ShipPlayer>();
		if ( sailor.IsValid() && sailor.CurrentStation.IsValid() ) return -100;
		float depth = Ocean.HeightAt( WorldPosition, Time.Now ) - WorldPosition.z;
		return depth > (controller.IsSwimming ? 18 : 30) ? 20 : -100;
	}

	public override void OnModeBegin() => Controller.IsSwimming = true;
	public override void OnModeEnd( MoveMode next ) => Controller.IsSwimming = false;

	public override void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = false;
		body.LinearDamping = 0;
		body.AngularDamping = 1;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		dive = Input.Down( "Duck" );
		if ( Input.Pressed( "Jump" ) ) TryWaterJump();
		return Rotation.FromYaw( eyes.Angles().yaw ) * input.WithZ( 0 ).ClampLength( 1 ) * SwimSpeed;
	}

	public override void AddVelocity()
	{
		if ( jumpLockout > 0 ) return;
		var body = Controller.Body;
		float height = Ocean.HeightAt( WorldPosition, Time.Now );
		float waveVelocity = (Ocean.HeightAt( WorldPosition, Time.Now + 0.02f ) - height) / 0.02f;
		float vertical = dive ? -110 : Math.Clamp( (height - FloatDepth - WorldPosition.z) * 5 + waveVelocity, -180, 240 );
		var target = Controller.WishVelocity.WithZ( vertical );
		body.Velocity += (target - body.Velocity) * (1 - MathF.Exp( -8 * Time.Delta ));
	}

	/// <summary>Leap at the surface. Nearby hull motion is inherited to allow boarding underway.</summary>
	public bool TryWaterJump()
	{
		if ( !Controller.IsSwimming || jumpLockout > 0 || !Ocean.IsValid() ) return false;
		if ( Ocean.HeightAt( WorldPosition, Time.Now ) - WorldPosition.z > FloatDepth + 25 ) return false;
		var direction = Controller.EyeAngles.ToRotation().Forward.WithZ( 0 ).Normal;
		var ahead = WorldPosition + direction * 110;
		var deck = Scene.Trace.Ray( ahead + Vector3.Up * 190, ahead + Vector3.Down * 30 )
			.IgnoreGameObjectHierarchy( GameObject ).Run();
		var ship = deck.GameObject.IsValid() ? deck.GameObject.GetComponentInParent<ArcadeShip>() : null;
		var velocity = Controller.WishVelocity.WithZ( 0 );
		if ( ship.IsValid() && ship.Body.IsValid() && deck.Normal.z > 0.6f )
		{
			velocity = ship.Body.GetVelocityAtPoint( WorldPosition ) + direction * SwimSpeed;
		}
		Controller.Body.Velocity = velocity.WithZ( MathF.Max( 0, velocity.z ) + WaterJumpSpeed );
		Controller.PreventGrounding( 0.2f );
		jumpLockout = 0.7f;
		dive = false;
		return true;
	}
}
