using System;

namespace NavalCombat;

public sealed partial class NavalWeather
{
	/// <summary>Scene sunlight used as a clear-day lighting template. It resumes when weather is disabled.</summary>
	[Property, Group( "Sky" )] public DirectionalLight Sun { get; set; }

	private DirectionalLight weatherLight;
	private DirectionalLight sunTemplate;

	private static Vector3 SolarDirectionAt( float hour )
	{
		float angle = (hour - 6f) * (MathF.PI / 12f);
		float elevation = MathF.Sin( angle );
		// A tilted orbital plane keeps noon shadows readable and avoids going through the zenith.
		return new Vector3( MathF.Cos( angle ), -0.5f * elevation, 0.8660254f * elevation );
	}

	public static Vector3 LightDirectionAt( float hour )
	{
		var sun = SolarDirectionAt( hour );
		return sun.z >= 0f ? sun : -sun;
	}

	public static float DaylightAt( float hour )
	{
		float level = Math.Clamp( (SolarDirectionAt( hour ).z + 0.16f) / 0.52f, 0f, 1f );
		return level * level * (3f - 2f * level);
	}

	private void UpdateSky( float hour, float cloudCover )
	{
		var sun = SolarDirectionAt( hour );
		float day = DaylightAt( hour );
		Scene.RenderAttributes.Set( "NavalSkySun", sun );
		Scene.RenderAttributes.Set( "NavalSkyDaylight", day );
		Scene.RenderAttributes.Set( "NavalSkyCover", cloudCover );
		Scene.RenderAttributes.Set( "NavalSkyTime", Time.Now );

		if ( sunTemplate != Sun )
		{
			if ( sunTemplate.IsValid() ) sunTemplate.Enabled = true;
			sunTemplate = Sun;
		}
		if ( Sun.IsValid() && Sun.Enabled ) Sun.Enabled = false;

		if ( !weatherLight.IsValid() )
		{
			var child = new GameObject( GameObject, true, "Weather lighting" );
			child.NetworkMode = NetworkMode.Never;
			child.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.Hidden;
			weatherLight = child.AddComponent<DirectionalLight>();
		}

		var clearSun = Sun.IsValid() ? Sun.LightColor : Color.White;
		var clearAmbient = Sun.IsValid() ? Sun.SkyColor : new Color( 0.25f, 0.32f, 0.35f );
		var moonlight = new Color( 0.18f, 0.27f, 0.47f );
		var direct = sun.z >= 0f
			? Color.Lerp( new Color( 1f, 0.49f, 0.22f ), clearSun, Math.Clamp( sun.z / 0.45f, 0f, 1f ) )
			: moonlight;
		// Fade the directional light through zero while the key switches between sun and moon.
		float height = Math.Clamp( MathF.Abs( sun.z ) / 0.18f, 0f, 1f );
		float storm = cloudCover * cloudCover;
		weatherLight.WorldRotation = Rotation.LookAt( -LightDirectionAt( hour ) );
		weatherLight.LightColor = direct.WithAlpha( 1f );
		weatherLight.Intensity = height * (1f - 0.76f * storm) * (Sun.IsValid() ? Sun.Intensity : 1f);
		weatherLight.SkyColor = Color.Lerp( new Color( 0.042f, 0.064f, 0.12f ),
			Color.Lerp( clearAmbient, new Color( 0.16f, 0.20f, 0.26f ), storm ), day ).WithAlpha( 1f );

		if ( Sun.IsValid() )
		{
			weatherLight.Shadows = Sun.Shadows;
			weatherLight.ContactShadows = Sun.ContactShadows;
			weatherLight.ShadowCascadeCount = Sun.ShadowCascadeCount;
			weatherLight.ShadowCascadeSplitRatio = Sun.ShadowCascadeSplitRatio;
		}
	}

	private void RestoreSky()
	{
		if ( weatherLight.IsValid() ) weatherLight.GameObject.Destroy();
		weatherLight = null;
		if ( sunTemplate.IsValid() ) sunTemplate.Enabled = true;
		sunTemplate = null;
		if ( !Scene.IsValid() ) return;
		Scene.RenderAttributes.Set( "NavalSkySun", SolarDirectionAt( 12f ) );
		Scene.RenderAttributes.Set( "NavalSkyDaylight", 1f );
		Scene.RenderAttributes.Set( "NavalSkyCover", 0.38f );
		Scene.RenderAttributes.Set( "NavalSkyTime", 0f );
	}
}
