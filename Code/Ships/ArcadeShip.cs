using System;

namespace NavalCombat;

/// <summary>Local handling test: four spring floats and assisted arcade steering.</summary>
public sealed class ArcadeShip : Component
{
	[Property] public ArcadeOcean Ocean { get; set; }
	[Property] public Rigidbody Body { get; set; }
	[Property] public ShipHelm Helm { get; set; }
	[Property] public float ForwardSpeed { get; set; } = 650;
	[Property] public SailRig Sails { get; set; }
	[Property] public float Acceleration { get; set; } = 260;
	[Property] public float TurnRate { get; set; } = 55;
	[Property] public float BuoyancySpring { get; set; } = 50;
	[Property] public float BuoyancyDamping { get; set; } = 12;

	private readonly Vector3[] floats =
	{
		new( 200, 90, -18 ), new( 200, -90, -18 ),
		new( -200, 90, -18 ), new( -200, -90, -18 )
	};
	private float steering;
	/// <summary>Only the occupied helm may submit controls; walking input never drives the ship.</summary>
	public void SetHelmInput( ShipHelm source, float turn )
	{
		if ( source != Helm || !source.IsValid() || !source.IsOccupied ) return;
		steering = Math.Clamp( turn, -1, 1 );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Body.IsValid() || !Ocean.IsValid() ) return;
		if ( !Helm.IsValid() || !Helm.Enabled || !Helm.IsOccupied )
		{
			steering = 0;
		}

		float mass = Body.Mass;
		// Custom gravity makes the spring equilibrium independent of project gravity settings.
		Body.ApplyForce( Vector3.Down * 900 * mass );
		int submerged = 0;
		foreach ( var sample in floats )
		{
			var point = WorldPosition + WorldRotation * sample;
			float depth = Ocean.HeightAt( point, Time.Now ) - point.z;
			if ( depth <= 0 ) continue;
			submerged++;
			float lift = Math.Clamp( depth * BuoyancySpring - Body.GetVelocityAtPoint( point ).z * BuoyancyDamping, 0, 2700 );
			Body.ApplyForceAt( point, Vector3.Up * lift * mass / floats.Length );
		}

		float wet = submerged / (float)floats.Length;
		var forward = WorldRotation.Forward.WithZ( 0 ).Normal;
		var velocity = Body.Velocity.WithZ( 0 );
		float speed = Vector3.Dot( velocity, forward );
		float desired = ForwardSpeed * (Sails.IsValid() ? Sails.DriveFraction : 0);
		float drive = Math.Clamp( (desired - speed) * (desired > 0 ? 1.0f : 0.25f), -Acceleration, Acceleration );
		var sideways = velocity - forward * speed;
		Body.ApplyForce( (forward * drive - sideways * 3) * mass * wet );

		// Preserve wave-induced roll/pitch, but directly assist yaw for responsive low-speed turns.
		var angular = Body.AngularVelocity;
		float yaw = steering * TurnRate * MathF.PI / 180 * wet;
		float blend = 1 - MathF.Exp( -6 * Time.Delta );
		angular.z += (yaw - angular.z) * blend;
		Body.AngularVelocity = angular;
	}
}
