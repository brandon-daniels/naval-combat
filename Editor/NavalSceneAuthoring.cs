using System;
using Editor;
using Sandbox;
namespace NavalCombat;

/// <summary>One-time conversion of the original prototype into authored scene objects.</summary>
public static class NavalSceneAuthoring
{
	[ConCmd( "naval_author_scene" )]
	public static void Build()
	{
		var session = SceneEditorSession.Active;
		if ( session is null || !session.Scene.IsEditor ) return;
		var setup = session.Scene.GetAllComponents<NavalPrototype>().FirstOrDefault();
		if ( !setup.IsValid() || setup.Ship.IsValid() ) return;
		using var scope = SceneEditorSession.Scope();
		var content = new GameObject( setup.GameObject, true, "Sailing world" );
		content.NetworkMode = NetworkMode.Never;
		var ocean = new GameObject( content, true, "Ocean" ).AddComponent<ArcadeOcean>();
		var wind = new GameObject( content, true, "Sailing wind" ).AddComponent<SailingWind>();
		new PracticeIslandAuthoring().Build( new GameObject( content, true, "Practice islands" ) );
		var ship = new GameObject( content, false, "Arcade ship" );
		ship.WorldPosition = new Vector3( 0, 0, 30 );
		var collider = ship.AddComponent<BoxCollider>();
		collider.Scale = new Vector3( 520, 240, 52 );
		var body = ship.AddComponent<Rigidbody>();
		body.MassOverride = 10000;
		body.Gravity = false;
		body.LinearDamping = 0.05f;
		body.AngularDamping = 1.8f;
		var controller = ship.AddComponent<ArcadeShip>();
		controller.Ocean = ocean;
		controller.Body = body;
		AddBox( ship, "Hull", Vector3.Zero, new Vector3( 520, 240, 52 ), new Color( 0.22f, 0.11f, 0.055f ) );
		AddBox( ship, "Deck", new Vector3( 0, 0, 30 ), new Vector3( 510, 234, 10 ), new Color( 0.65f, 0.40f, 0.19f ), true );
		AddBox( ship, "Cabin", new Vector3( -190, 0, 70 ), new Vector3( 90, 100, 70 ), new Color( 0.75f, 0.71f, 0.56f ), true );
		var railColor = new Color( 0.32f, 0.18f, 0.08f );
		AddBox( ship, "Port rail", new Vector3( 0, 115, 57 ), new Vector3( 510, 8, 44 ), railColor, true );
		AddBox( ship, "Starboard rail", new Vector3( 0, -115, 57 ), new Vector3( 510, 8, 44 ), railColor, true );
		AddBox( ship, "Bow rail", new Vector3( 251, 0, 57 ), new Vector3( 8, 224, 44 ), railColor, true );
		AddBox( ship, "Stern rail", new Vector3( -251, 0, 57 ), new Vector3( 8, 224, 44 ), railColor, true );

		var helm = CreateHelm( ship, controller );
		controller.Helm = helm;
		controller.Sails = CreateMast( ship, controller, wind );
		var spawn = new GameObject( ship, true, "Deck spawn" );
		spawn.LocalPosition = new Vector3( -65, 0, 40 );
		ship.Enabled = true;
		ocean.FollowTarget = ship;
		setup.Ship = controller;
		setup.Ocean = ocean;
		setup.Wind = wind;
		setup.SpawnPoint = spawn;
		Log.Info( "Authored ship and islands created. Save the scene." );
	}
	private static SailRig CreateMast( GameObject ship, ArcadeShip controller, SailingWind wind )
	{
		var mast = new GameObject( ship, true, "Mast and sail controls" );
		mast.LocalPosition = new Vector3( -20, 50, 35 );
		var rig = mast.AddComponent<SailRig>();
		rig.Ship = controller;
		rig.Wind = wind;
		rig.SitPose = BaseChair.AnimatorSitPose.Standing;
		rig.TooltipTitle = "Work the sails";
		rig.SeatPosition = new GameObject( mast, true, "Sailor rigging position" );
		rig.SeatPosition.LocalPosition = new Vector3( -28, -35, 1 );
		rig.EyePosition = new GameObject( mast, true, "Rigging eyes" );
		rig.EyePosition.LocalPosition = new Vector3( -28, -35, 65 );
		var exit = new GameObject( mast, true, "Rigging exit" );
		exit.LocalPosition = new Vector3( -55, -40, 6 );
		rig.ExitPoints = new[] { exit };
		var collider = mast.AddComponent<BoxCollider>();
		collider.Center = new Vector3( 0, 0, 130 );
		collider.Scale = new Vector3( 14, 14, 260 );
		var wood = new Color( 0.30f, 0.17f, 0.08f );
		AddBox( mast, "Mast", new Vector3( 0, 0, 130 ), new Vector3( 14, 14, 260 ), wood );
		AddBox( mast, "Brass rigging cleat", new Vector3( -9, -9, 40 ), new Vector3( 8, 28, 8 ), new Color( 0.95f, 0.64f, 0.18f ) );
		rig.Yard = new GameObject( mast, true, "Rotating sail yard" );
		AddBox( rig.Yard, "Upper yard", new Vector3( 0, 0, 230 ), new Vector3( 10, 210, 10 ), wood );
		rig.LowerBoom = AddBox( rig.Yard, "Lower sail boom", new Vector3( 0, 0, 222 ), new Vector3( 6, 194, 6 ), wood );
		rig.WindArrow = new GameObject( mast, true, "Wind direction arrow" );
		rig.WindArrow.LocalPosition = new Vector3( 0, 0, 278 );
		var orange = new Color( 1, 0.52f, 0.1f );
		AddBox( rig.WindArrow, "Arrow shaft", new Vector3( 15, 0, 0 ), new Vector3( 70, 5, 5 ), orange );
		for ( int side = -1; side <= 1; side += 2 )
		{
			var head = AddBox( rig.WindArrow, "Arrow head", new Vector3( 40, side * 9, 0 ), new Vector3( 28, 5, 5 ), orange );
			head.LocalRotation = Rotation.FromYaw( -side * 40 );
		}
		return rig;
	}

	private static ShipHelm CreateHelm( GameObject ship, ArcadeShip controller )
	{
		var helmObject = new GameObject( ship, true, "Bow helm" );
		helmObject.LocalPosition = new Vector3( 170, 0, 35 );
		var helm = helmObject.AddComponent<ShipHelm>();
		helm.Ship = controller;
		helm.SitPose = BaseChair.AnimatorSitPose.Standing;
		helm.TooltipTitle = "Take the helm";
		helm.SeatPosition = new GameObject( helmObject, true, "Helmsman position" );
		helm.SeatPosition.LocalPosition = new Vector3( -34, 0, 1 );
		helm.EyePosition = new GameObject( helmObject, true, "Helmsman eyes" );
		helm.EyePosition.LocalPosition = new Vector3( -34, 0, 65 );
		var exit = new GameObject( helmObject, true, "Helm exit" );
		exit.LocalPosition = new Vector3( -80, 0, 6 );
		helm.ExitPoints = new[] { exit };
		var console = helmObject.AddComponent<BoxCollider>();
		console.Center = new Vector3( 6, 0, 28 );
		console.Scale = new Vector3( 16, 44, 56 );
		AddBox( helmObject, "Helm pedestal", new Vector3( 8, 0, 23 ), new Vector3( 16, 18, 46 ), new Color( 0.25f, 0.14f, 0.07f ) );
		var brass = new Color( 0.95f, 0.64f, 0.18f );
		for ( int i = 0; i < 8; i++ )
		{
			float angle = i * MathF.PI / 4;
			var segment = AddBox( helmObject, "Wheel rim", new Vector3( 0, MathF.Sin( angle ) * 20, 50 + MathF.Cos( angle ) * 20 ), new Vector3( 5, 18, 5 ), brass );
			segment.LocalRotation = Rotation.FromRoll( -i * 45 );
		}
		AddBox( helmObject, "Wheel spoke", new Vector3( 0, 0, 50 ), new Vector3( 5, 40, 4 ), brass );
		AddBox( helmObject, "Wheel spoke", new Vector3( 0, 0, 50 ), new Vector3( 5, 4, 40 ), brass );
		helm.LeftGrip = new GameObject( helmObject, true, "Left grip" );
		helm.LeftGrip.LocalPosition = new Vector3( -3, 18, 50 );
		helm.RightGrip = new GameObject( helmObject, true, "Right grip" );
		helm.RightGrip.LocalPosition = new Vector3( -3, -18, 50 );
		return helm;
	}

	private static GameObject AddBox( GameObject parent, string name, Vector3 position, Vector3 size, Color color, bool solid = false )
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

