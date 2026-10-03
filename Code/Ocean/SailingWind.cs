using System;

namespace NavalCombat;

/// <summary>World-space wind. Direction always means where the air is blowing TOWARD.</summary>
public sealed class SailingWind : Component
{
	[Property] public float Heading { get; set; } = 25;
	[Property] public float Strength { get; set; } = 1;
	[Property] public float GustAmount { get; set; } = 0.12f;
	public Vector3 Direction => Rotation.FromYaw( Heading ).Forward;
	public float CurrentStrength => Math.Clamp( Strength, 0, 2 )
		* (1 + Math.Clamp( GustAmount, 0, 0.4f ) * MathF.Sin( Time.Now * 0.23f ) * MathF.Sin( Time.Now * 0.41f ));
}
