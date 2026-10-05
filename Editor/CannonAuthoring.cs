using System;
using Sandbox;
using Editor;
namespace NavalCombat;
public static class CannonAuthoring
{
	[ConCmd( "naval_add_cannons" )]
	public static void Build()
	{
		var session = SceneEditorSession.Active;
		if ( session is null || !session.Scene.IsEditor ) return;
		var ship = session.Scene.GetAllComponents<ArcadeShip>().FirstOrDefault();
		if ( !ship.IsValid() || session.Scene.GetAllComponents<ShipCannon>().Any() ) return;
		using var scope = SceneEditorSession.Scope();
		foreach ( int side in new[] { -1, 1 } )
		{
			var root = new GameObject( ship.GameObject, true, $"{(side > 0 ? "Port" : "Starboard")} cannon" );
			root.LocalPosition = new Vector3( 0, side * 83, 35 );
			root.LocalRotation = Rotation.FromYaw( side * 90 );
			var cannon = root.AddComponent<ShipCannon>();
			cannon.Ship = ship;
			cannon.UseDistance = 48;
			cannon.SitPose = BaseChair.AnimatorSitPose.Standing;
			cannon.SeatPosition = Anchor( root, "Gunner position", new Vector3( -52, 0, 1 ) );
			cannon.EyePosition = Anchor( root, "Gunner eyes", new Vector3( -52, 0, 66 ) );
			cannon.ExitPoints = new[] { Anchor( root, "Gunner exit", new Vector3( -58, 0, 6 ) ) };
			var collider = root.AddComponent<BoxCollider>();
			collider.Center = new Vector3( 0, 0, 24 );
			collider.Scale = new Vector3( 44, 44, 48 );
			Box( root, "Wood carriage", new Vector3( 0, 0, 25 ), new Vector3( 48, 44, 24 ), new Color( 0.35f, 0.19f, 0.08f ) );
			Box( root, "Iron support", new Vector3( 0, 0, 48 ), new Vector3( 15, 35, 40 ), new Color( 0.15f, 0.17f, 0.19f ) );
			cannon.Barrel = Anchor( root, "Aiming barrel", new Vector3( 0, 0, 65 ) );
			cannon.Barrel.LocalRotation = Rotation.FromPitch( -12 );
			for ( int i = 0; i < 8; i++ )
			{
				float a = i * MathF.PI / 4;
				var stave = Box( cannon.Barrel, "Barrel iron", new Vector3( 20, MathF.Sin( a ) * 14, MathF.Cos( a ) * 14 ), new Vector3( 90, 12, 8 ), new Color( 0.12f, 0.15f, 0.18f ) );
				stave.LocalRotation = Rotation.FromRoll( -i * 45 );
			}
			Box( cannon.Barrel, "Breech", new Vector3( -25, 0, 0 ), new Vector3( 8, 26, 26 ), new Color( 0.09f, 0.10f, 0.12f ) );
			cannon.Muzzle = Anchor( cannon.Barrel, "Muzzle", new Vector3( 75, 0, 0 ) );
		}
		Log.Info( "Two saved cannon stations added." );
	}
	private static GameObject Anchor( GameObject parent, string name, Vector3 position )
	{
		var go = new GameObject( parent, true, name );
		go.LocalPosition = position;
		return go;
	}
	private static GameObject Box( GameObject parent, string name, Vector3 p, Vector3 scale, Color color )
	{
		var go = Anchor( parent, name, p );
		go.LocalScale = scale / 50;
		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = color;
		return go;
	}
}
