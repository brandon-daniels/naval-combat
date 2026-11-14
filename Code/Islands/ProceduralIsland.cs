using System;

namespace NavalCombat;

public enum IslandPreset
{
	[Title( "Palm Cove" )] PalmCove,
	[Title( "Farmer's Bay" )] FarmersBay,
	[Title( "Granite Headland" )] GraniteHeadland,
	[Title( "Smuggler's Hollow" )] SmugglersHollow
}

public enum IslandFeatureMode { Preset, Always, Never }

/// <summary>A seeded island with a reserved beach landing. Place its origin at mean sea level.</summary>
[Title( "Procedural Island" ), Category( "Naval" ), Icon( "landscape" )]
public sealed class ProceduralIsland : Component, Component.ExecuteInEditor
{
	[Property] public IslandPreset Preset { get; set; } = IslandPreset.PalmCove;
	/// <summary>The same seed and settings recreate the same terrain and details.</summary>
	[Property, Editor( "naval-island-seed" )] public int Seed { get; set; } = 12345;
	/// <summary>Scales the landform. Dock planks and other props retain their human scale.</summary>
	[Property, Range( 0.75f, 1.75f )] public float Size { get; set; } = 1;
	[Property, Group( "Features" )] public IslandFeatureMode Dock { get; set; }
	[Property, Group( "Features" )] public IslandFeatureMode Field { get; set; }
	[Property, Group( "Features" )] public IslandFeatureMode Cave { get; set; }

	private ModelRenderer renderer;
	private ModelCollider collider;
	private (IslandPreset, int, float, IslandFeatureMode, IslandFeatureMode, IslandFeatureMode)? builtSettings;
	internal float NavigationRadius { get; private set; }
	internal IslandTerrain Terrain { get; private set; }

	protected override void OnEnabled() => UpdateIsland();
	protected override void OnRefresh() => builtSettings = null;
	protected override void OnUpdate() => UpdateIsland();

	private void UpdateIsland()
	{
		float scale = float.IsFinite( Size ) ? Math.Clamp( Size, 0.75f, 1.75f ) : 1;
		var settings = (Preset, Seed, scale, Dock, Field, Cave);
		if ( builtSettings == settings && Terrain is not null && renderer.IsValid() && collider.IsValid() ) return;

		var (radius, height, cliffs, dockChance, fieldChance, caveChance) = Preset switch
		{
			IslandPreset.FarmersBay => (2100f, 400f, 0.32f, 0.70f, 1f, 0.15f),
			IslandPreset.GraniteHeadland => (2550f, 950f, 0.95f, 0.15f, 0.12f, 0.60f),
			IslandPreset.SmugglersHollow => (1850f, 680f, 0.78f, 0.40f, 0.18f, 1f),
			_ => (1450f, 300f, 0.25f, 0.25f, 0.30f, 0.20f)
		};
		var rolls = new Random( Seed ^ 0x12da5bc );
		bool hasDock = Include( Dock, rolls.NextDouble() < dockChance );
		bool hasField = Include( Field, rolls.NextDouble() < fieldChance );
		bool hasCave = Include( Cave, rolls.NextDouble() < caveChance );

		var terrain = new IslandTerrain( Seed, radius * scale, height * scale, cliffs, hasField, hasCave );
		var geometry = new IslandGeometry();
		terrain.Emit( ( a, b, c, color ) => geometry.Triangle( a, b, c, color ) );
		IslandFeatures.Build( terrain, geometry, Seed, hasDock, hasField );
		var model = geometry.CreateModel();

		if ( !renderer.IsValid() )
		{
			renderer = GameObject.AddComponent<ModelRenderer>();
			renderer.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.Hidden;
		}
		if ( !collider.IsValid() )
		{
			collider = GameObject.AddComponent<ModelCollider>();
			collider.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked | ComponentFlags.Hidden;
		}
		renderer.Model = model;
		collider.Model = model;
		Terrain = terrain;
		NavigationRadius = geometry.SolidRadius;
		builtSettings = settings;
	}

	private static bool Include( IslandFeatureMode mode, bool presetChoice ) =>
		mode == IslandFeatureMode.Always || (mode == IslandFeatureMode.Preset && presetChoice);

	protected override void OnDisabled() => ReleaseGeometry();
	protected override void OnDestroy() => ReleaseGeometry();

	private void ReleaseGeometry()
	{
		if ( renderer.IsValid() ) renderer.Destroy();
		if ( collider.IsValid() ) collider.Destroy();
		renderer = null;
		collider = null;
		builtSettings = null;
		NavigationRadius = 0;
		Terrain = null;
	}
}
