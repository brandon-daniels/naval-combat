using System;

namespace NavalCombat;

public sealed class ShipChaseCamera : Component
{
	[Property] public GameObject Target { get; set; }
	[Property] public float Distance { get; set; } = 620;
	[Property] public float Height { get; set; } = 320;
	private bool initialized;

	protected override void OnUpdate()
	{
		if ( !Target.IsValid() ) return;
		var forward = Target.WorldRotation.Forward.WithZ( 0 ).Normal;
		var desired = Target.WorldPosition - forward * Distance + Vector3.Up * Height;
		WorldPosition = initialized
			? Vector3.Lerp( WorldPosition, desired, 1 - MathF.Exp( -5 * Time.Delta ) ) : desired;
		WorldRotation = Rotation.LookAt( Target.WorldPosition + Vector3.Up * 40 - WorldPosition );
		initialized = true;
	}
}
