using Sandbox.Movement;

namespace NavalCombat;

/// <summary>Walk in the supporting hull's velocity frame, including roll and yaw.</summary>
public sealed class DeckWalkMode : MoveModeWalk
{
	public ArcadeShip SupportingShip => Controller.GroundObject.IsValid()
		? Controller.GroundObject.GetComponentInParent<ArcadeShip>() : null;

	public override void UpdateRigidBody( Rigidbody body )
	{
		base.UpdateRigidBody( body );
		if ( !SupportingShip.IsValid() ) return;
		// Gravity and world-space damping fight a moving deck and cause sliding.
		body.Gravity = false;
		body.LinearDamping = 0;
	}

	public override void AddVelocity()
	{
		var ship = SupportingShip;
		if ( !ship.IsValid() || !ship.Body.IsValid() )
		{
			base.AddVelocity();
			return;
		}
		var normal = ship.WorldRotation.Up;
		var wish = Controller.WishVelocity.WithZ( 0 );
		// Follow the slope without reducing the player's horizontal walking speed.
		wish.z = -Vector3.Dot( wish, normal ) / System.MathF.Max( normal.z, 0.25f );
		Controller.GroundVelocity = ship.Body.GetVelocityAtPoint( WorldPosition );
		Controller.Body.Velocity = Controller.GroundVelocity + wish;
	}

	public override void PostPhysicsStep()
	{
		// Only grounded players snap down; Jump clears ground before physics runs.
		StickToGround( SupportingShip.IsValid() ? 32 : StepDownHeight );
	}
}
