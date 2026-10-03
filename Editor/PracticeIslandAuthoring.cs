using System;
using System.Collections.Generic;

namespace NavalCombat;

/// <summary>Three fixed, collidable landmarks with broad sailing channels between them.</summary>
public sealed class PracticeIslandAuthoring
{
	private GameObject GameObject;
	public void Build( GameObject parent )
	{
		GameObject = parent;
		var beacon = Island( "Beacon Island", new Vector3( 2600, -1500, 0 ), 720, 0 );
		Box( beacon, "Lighthouse", new Vector3( 0, 0, 440 ), new Vector3( 110, 110, 300 ), new Color( 0.92f, 0.86f, 0.68f ) );
		Box( beacon, "Red beacon band", new Vector3( 0, 0, 500 ), new Vector3( 114, 114, 55 ), new Color( 0.8f, 0.18f, 0.10f ) );
		Box( beacon, "Lantern", new Vector3( 0, 0, 625 ), new Vector3( 95, 95, 70 ), new Color( 1, 0.72f, 0.22f ) );
		Box( beacon, "Lantern roof", new Vector3( 0, 0, 675 ), new Vector3( 155, 155, 25 ), new Color( 0.25f, 0.16f, 0.12f ) );

		var rocks = Island( "Twin Rocks Island", new Vector3( 5800, 2000, 0 ), 860, 1.3f );
		Mound( rocks, "Tall rock", new Vector3( -150, 0, 260 ), 170, 460, new Color( 0.32f, 0.38f, 0.42f ), 0.3f );
		Mound( rocks, "Short rock", new Vector3( 190, 70, 260 ), 140, 310, new Color( 0.43f, 0.48f, 0.5f ), 1.7f );

		var palms = Island( "Palm Island", new Vector3( 9500, 700, 0 ), 760, 2.6f );
		for ( int i = 0; i < 5; i++ )
		{
			float angle = i * 2.4f;
			var foot = new Vector3( MathF.Cos( angle ) * 270, MathF.Sin( angle ) * 220, 270 );
			Box( palms, "Palm trunk", foot + Vector3.Up * 145, new Vector3( 28, 28, 290 ), new Color( 0.43f, 0.25f, 0.12f ) );
			for ( int leaf = 0; leaf < 6; leaf++ )
			{
				var rotation = Rotation.FromYaw( leaf * 60 + i * 25 );
				var frond = Box( palms, "Palm frond", foot + Vector3.Up * 290 + rotation.Forward * 85,
					new Vector3( 210, 45, 12 ), new Color( 0.18f, 0.48f, 0.22f ), false );
				frond.LocalRotation = rotation * Rotation.FromPitch( 12 );
			}
		}
	}

	private GameObject Island( string name, Vector3 location, float radius, float phase )
	{
		var island = new GameObject( GameObject, true, name );
		island.LocalPosition = location;
		Ring( island, "Submerged shore", radius, 1.15f, -180, 1, 25, new Color( 0.55f, 0.43f, 0.27f ), phase );
		Ring( island, "Sandy beach", radius, 1, 25, 0.78f, 150, new Color( 0.88f, 0.74f, 0.43f ), phase );
		Ring( island, "Grassy slope", radius, 0.78f, 150, 0.45f, 275, new Color( 0.28f, 0.49f, 0.22f ), phase );
		Ring( island, "Grassy summit", radius, 0.45f, 275, 0, 300, new Color( 0.38f, 0.59f, 0.27f ), phase );
		return island;
	}

	private static void Mound( GameObject parent, string name, Vector3 position, float radius, float height, Color color, float phase )
	{
		var root = new GameObject( parent, true, name );
		root.LocalPosition = position;
		Ring( root, "Rock base", radius, 1, 0, 0.7f, height * 0.7f, color, phase );
		Ring( root, "Rock peak", radius, 0.7f, height * 0.7f, 0, height, color, phase );
	}

	private static void Ring( GameObject parent, string name, float radius, float outer, float low, float inner, float high, Color color, float phase )
	{
		var section = new GameObject( parent, true, name );
		var surface = section.AddComponent<IslandSurface>();
		surface.Radius = radius;
		surface.OuterScale = outer;
		surface.InnerScale = inner;
		surface.LowerHeight = low;
		surface.UpperHeight = high;
		surface.Phase = phase;
		surface.Tint = color;
	}

	private static GameObject Box( GameObject parent, string name, Vector3 position, Vector3 size, Color color, bool solid = true )
	{
		var part = new GameObject( parent, true, name );
		part.LocalPosition = position;
		part.LocalScale = size / 50;
		var renderer = part.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = color;
		if ( solid ) part.AddComponent<BoxCollider>().Scale = new Vector3( 50, 50, 50 );
		return part;
	}
}
