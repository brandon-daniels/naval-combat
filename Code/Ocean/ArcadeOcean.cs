using System;
using System.Linq;

namespace NavalCombat;

/// <summary>Cel shaded swells sharing their height and clock with ship buoyancy and foam.</summary>
public sealed class ArcadeOcean : Component, Component.ExecuteInEditor
{
	[Property] public float SeaLevel { get; set; } = 0;
	[Property] public float WaveHeight { get; set; } = 55;
	[Property] public float FoamStrength { get; set; } = 1;

	/// <summary>Changes the directions, spacing and starting phases of the swell groups.</summary>
	[Property] public int WaveSeed { get; set; } = 731;
	/// <summary>Strength of the forward pitch at the crest. Storm packets produce the steepest faces.</summary>
	[Property, Range( 0f, 1f )] public float CrestSteepness { get; set; } = 0.86f;
	/// <summary>Angular separation between the prevailing swell and crossing wave trains.</summary>
	[Property, Range( 0.5f, 1.5f )] public float DirectionalSpread { get; set; } = 1f;
	[Property] public GameObject FollowTarget { get; set; }

	public const float ViewDistance = 44000f;
	private const float HalfExtent = 50000f;
	private const int Cells = 144;
	private const float Spacing = 36f;
	private static readonly Vector3 DefaultLightDirection = new Vector3( -0.4f, -0.25f, 0.8f ).Normal;
	private float waveScale = 1;
	private float foamScale = 1;
	private float daylight = 1;
	private float storminess;
	private Vector3 lightDirection = DefaultLightDirection;
	private OceanWaveField waveField;
	private int cachedSeed;
	private float cachedSpread;
	private float EffectiveWaveHeight => WaveHeight * waveScale;
	private float CrestSharpness => Math.Clamp( CrestSteepness * (0.90f + 0.25f * storminess), 0f, 1f )
		* Math.Clamp( waveScale, 0f, 1f );
	private Vertex[] vertices;
	private float[] gridCoordinates;
	private float[] cellSpacings;
	private OceanWaveField.Sample[] gridSamples;
	private Vector4[] columnPhases;
	private Mesh mesh;
	private ModelRenderer renderer;
	private OceanBreakSpray spray;
	private OceanShipWakes wakes;
	private OceanShoals shoals;

	/// <summary>Scales the authored sea state and sets the water lighting without changing inspector values.</summary>
	public void ApplyWeather( float waveScale, float foamScale, float daylight, float storminess, Vector3? lightDirection = null )
	{
		this.waveScale = Math.Max( 0, waveScale );
		this.foamScale = Math.Max( 0, foamScale );
		this.daylight = Math.Clamp( daylight, 0, 1 );
		this.storminess = Math.Clamp( storminess, 0, 1 );
		this.lightDirection = lightDirection ?? DefaultLightDirection;
	}

	public float HeightAt( Vector3 position, float time ) =>
		SeaLevel + EffectiveWaveHeight * GetWaveField().Height( position, time, CrestSharpness );

	public bool TrySampleBreakingCrossing( Vector3 position, float time, out float strength, out Vector2 travelDirection )
	{
		float energy = Math.Clamp( (MathF.Abs( EffectiveWaveHeight ) - 4f) / 16f, 0f, 1f );
		if ( energy <= 0 )
		{
			strength = 0;
			travelDirection = Vector2.Zero;
			return false;
		}
		strength = GetWaveField().CrossingAt( position, time, CrestSharpness, out travelDirection ) * energy;
		return strength > 0.02f;
	}

	private OceanWaveField GetWaveField()
	{
		float spread = Math.Clamp( DirectionalSpread, 0.5f, 1.5f );
		if ( waveField is null || cachedSeed != WaveSeed || cachedSpread != spread )
		{
			cachedSeed = WaveSeed;
			cachedSpread = spread;
			waveField = new OceanWaveField( WaveSeed, spread );
		}
		return waveField;
	}

	/// <summary>Rebuild the directional bands from the current code and inspector settings on the next sample.</summary>
	public void InvalidateWaveField() => waveField = null;

	protected override void OnRefresh()
	{
		InvalidateWaveField();
		if ( mesh is not null ) BuildSurface();
	}

	private void BuildSurface()
	{
		if ( renderer.IsValid() ) renderer.Destroy();
		vertices = new Vertex[(Cells + 1) * (Cells + 1)];
		gridCoordinates = new float[Cells + 1];
		cellSpacings = new float[Cells + 1];
		gridSamples = new OceanWaveField.Sample[vertices.Length];
		columnPhases = new Vector4[gridCoordinates.Length];
		for ( int i = 0; i <= Cells; i++ ) gridCoordinates[i] = GridCoordinate( i );
		for ( int i = 0; i <= Cells; i++ )
		{
			float left = gridCoordinates[i] - gridCoordinates[Math.Max( 0, i - 1 )];
			float right = gridCoordinates[Math.Min( Cells, i + 1 )] - gridCoordinates[i];
			cellSpacings[i] = Math.Max( left, right );
		}
		mesh = new Mesh( Material.Load( "materials/ocean/naval_ocean.vmat" ) );
		UpdateVertices();
		mesh.CreateVertexBuffer<Vertex>( vertices.Length, vertices );
		var indices = new int[Cells * Cells * 6];
		int index = 0;
		for ( int y = 0; y < Cells; y++ )
		{
			for ( int x = 0; x < Cells; x++ )
			{
				int a = y * (Cells + 1) + x;
				indices[index++] = a;
				indices[index++] = a + 1;
				indices[index++] = a + Cells + 2;
				indices[index++] = a;
				indices[index++] = a + Cells + 2;
				indices[index++] = a + Cells + 1;
			}
		}
		mesh.CreateIndexBuffer( indices.Length, indices );
		renderer = GameObject.AddComponent<ModelRenderer>();
		renderer.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		renderer.Model = Model.Builder.AddMesh( mesh ).Create();
		UpdateMaterial();
	}

	protected override void OnUpdate()
	{
		if ( mesh is null || !renderer.IsValid() || vertices is null || vertices.Length != (Cells + 1) * (Cells + 1)
			|| gridSamples is null || columnPhases is null || cellSpacings is null )
		{
			BuildSurface();
			return;
		}
		UpdateVertices();
		mesh.SetVertexBufferData<Vertex>( vertices );
		UpdateMaterial();
		(spray ??= new OceanBreakSpray()).Update( this, GetWaveField(), WorldPosition, Time.Now, CrestSharpness, EffectiveWaveHeight, daylight, storminess, foamScale * FoamStrength );
		(wakes ??= new OceanShipWakes()).Update( this, Time.Now, daylight, storminess );
	}

	private void UpdateMaterial()
	{
		if ( !renderer.IsValid() || !renderer.SceneObject.IsValid() ) return;
		// Mask the eight closest hull interiors, including other players' vessels.
		var target = FollowTarget.IsValid() ? FollowTarget.WorldPosition : WorldPosition;
		var ships = Scene.GetAllComponents<ArcadeShip>().Where( ship => ship.Active && ship.WalkableModelReady && !ship.IsSinking )
			.OrderBy( ship => (ship.WorldPosition - target).LengthSquared ).Take( 8 ).ToArray();
		for ( int i = 0; i < 8; i++ )
		{
			var x = Vector4.Zero;
			var y = Vector4.Zero;
			var z = Vector4.Zero;
			if ( i < ships.Length )
			{
				// Full inverse model transform: pitch, roll, authored yaw and scale all matter.
				var transform = ships[i].WalkableModelObject.WorldTransform;
				var origin = transform.PointToLocal( Vector3.Zero );
				var dx = transform.PointToLocal( Vector3.Forward ) - origin;
				var dy = transform.PointToLocal( Vector3.Left ) - origin;
				var dz = transform.PointToLocal( Vector3.Up ) - origin;
				x = new Vector4( dx.x, dy.x, dz.x, origin.x );
				y = new Vector4( dx.y, dy.y, dz.y, origin.y );
				z = new Vector4( dx.z, dy.z, dz.z, origin.z );
			}
			renderer.SceneObject.Attributes.Set( $"HullLocalX{i}", x );
			renderer.SceneObject.Attributes.Set( $"HullLocalY{i}", y );
			renderer.SceneObject.Attributes.Set( $"HullLocalZ{i}", z );
		}
		renderer.SceneObject.Attributes.Set( "OceanSeaLevel", SeaLevel );
		renderer.SceneObject.Attributes.Set( "OceanTime", Time.Now );
		renderer.SceneObject.Attributes.Set( "OceanWaveHeight", EffectiveWaveHeight );
		renderer.SceneObject.Attributes.Set( "OceanFoamStrength", Math.Max( 0, FoamStrength ) * foamScale );
		renderer.SceneObject.Attributes.Set( "OceanHasBreakers", 1f );
		renderer.SceneObject.Attributes.Set( "OceanTravelDirection", GetWaveField().TravelDirection );
		(shoals ??= new OceanShoals()).Apply( this, renderer );
		ApplyWaterLighting( renderer );
	}

	internal void ApplyWaterLighting( ModelRenderer water )
	{
		water.SceneObject.Attributes.Set( "OceanDaylight", daylight );
		water.SceneObject.Attributes.Set( "OceanStorminess", storminess );
		water.SceneObject.Attributes.Set( "OceanLightDirection", lightDirection );
	}

	private static float GridCoordinate( int index )
	{
		float unit = (index - Cells / 2) / (float)(Cells / 2);
		// Dense around the player, stretched at the horizon: no extra far-water draw calls.
		return (index - Cells / 2) * Spacing + MathF.Sign( unit ) * MathF.Pow( MathF.Abs( unit ), 12 )
			* (HalfExtent - Cells / 2 * Spacing);
	}

	private void UpdateVertices()
	{
		var target = FollowTarget.IsValid() ? FollowTarget.WorldPosition : Vector3.Zero;
		var center = new Vector3( MathF.Floor( target.x / Spacing ) * Spacing,
			MathF.Floor( target.y / Spacing ) * Spacing, 0 );
		// Keep the mesh near its object origin so model bounds remain stable as the ship travels.
		WorldPosition = center;
		var field = GetWaveField();
		float amplitude = EffectiveWaveHeight;
		float sharpness = CrestSharpness;
		field.SampleGrid( gridCoordinates, center, Time.Now, sharpness, gridSamples, columnPhases, cellSpacings );
		for ( int y = 0; y <= Cells; y++ )
		{
			for ( int x = 0; x <= Cells; x++ )
			{
				var local = new Vector3( gridCoordinates[x], gridCoordinates[y], 0 );
				int index = y * (Cells + 1) + x;
				var sample = gridSamples[index];
				local.z = SeaLevel + amplitude * sample.Height;
				var normal = new Vector3( -sample.Dx * amplitude, -sample.Dy * amplitude, 1 ).Normal;
				// The primary UV carries local travel direction weighted by breaking strength.
				vertices[index] = new Vertex( local, normal,
					Vector3.Forward, new Vector4( sample.Breaker.x, sample.Breaker.y, 0, 0 ) );
			}
		}
		float half = GridCoordinate( Cells );
		float maxHeight = MathF.Abs( amplitude ) * field.MaximumAmplitude + 32;
		mesh.Bounds = new BBox( new Vector3( -half, -half, SeaLevel - maxHeight ),
			new Vector3( half, half, SeaLevel + maxHeight ) );
	}

	protected override void OnDisabled()
	{
		if ( renderer.IsValid() ) renderer.Destroy();
		spray?.Clear();
		spray = null;
		wakes?.Clear();
		wakes = null;
		mesh = null;
	}
}
