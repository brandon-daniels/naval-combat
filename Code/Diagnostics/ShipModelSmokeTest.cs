using System;
using System.Linq;

namespace NavalCombat;

/// <summary>Checks the authored ship model, cargo collision and station layout in a running scene.</summary>
public static class ShipModelSmokeTest
{
	[ConCmd( "naval_test_ship_model" )]
	public static void Run()
	{
		if ( !Game.IsEditor || Game.ActiveScene is null || Game.ActiveScene.IsEditor ) return;
		try
		{
			var ship = Game.ActiveScene.GetAllComponents<ArcadeShip>()
				.FirstOrDefault( candidate => candidate.Active );
			Check( ship.IsValid(), "An enabled arcade ship exists" );

			Check( ship.WalkableModelReady, "Pirate ship model supplies the moving compound collision" );
			var rootBox = ship.GetComponent<BoxCollider>();
			Check( !rootBox.IsValid() || !rootBox.Enabled, "Interior-blocking prototype hull box is disabled" );

			// The visual is authored at -90 yaw, scale 24.4 and Z -45. Convert the
			// cargo-room source point explicitly into the rigidbody's local frame.
			var cargoTop = ship.WorldTransform.PointToWorld( new Vector3( 4 * ship.ShipModelScale, 0, 2.7f * ship.ShipModelScale + ship.ShipModelVerticalOffset ) );
			var cargoBottom = ship.WorldTransform.PointToWorld( new Vector3( 4 * ship.ShipModelScale, 0, -0.2f * ship.ShipModelScale + ship.ShipModelVerticalOffset ) );
			var cargoFloor = ship.Scene.Trace.Ray( cargoTop, cargoBottom ).Run();
			Log.Info( $"SHIP MODEL TEST: local bounds={ship.WalkableModelLocalBounds}, world bounds={ship.WalkableModelBounds}, cargo ray={cargoTop} to {cargoBottom}, hit={cargoFloor.Hit}" );
			Check( cargoFloor.Hit && cargoFloor.GameObject.IsDescendant( ship.GameObject ), "Below-deck cargo floor is collidable" );

			var cannons = Game.ActiveScene.GetAllComponents<ShipCannon>()
				.Where( cannon => cannon.Enabled && cannon.Ship == ship ).ToArray();
			Check( cannons.Length == 2, "One usable cannon is present on each broadside" );
			var cannonSides = cannons.Select( cannon => ship.WorldTransform.PointToLocal( cannon.WorldPosition ).y ).ToArray();
			Check( cannonSides.Min() < -50 && cannonSides.Max() > 50, "Cannons face from opposite sides of the hull" );

			Check( ship.Helm.IsValid() && ship.Helm.Enabled && ship.Helm.Ship == ship, "Helm remains usable" );
			Check( ship.Sails.IsValid() && ship.Sails.Enabled && ship.Sails.Ship == ship, "Raise/lower sail station remains usable" );
			var sailor = Game.ActiveScene.GetAllComponents<ShipPlayer>().FirstOrDefault( candidate => candidate.Ship == ship );
			if ( sailor.IsValid() )
			{
				Log.Info( $"SHIP MODEL TEST: sailor local={ship.WorldTransform.PointToLocal( sailor.WorldPosition )}, spawn local={ship.WorldTransform.PointToLocal( sailor.SpawnPoint.WorldPosition )}, grounded={sailor.Controller.IsOnGround}, swimming={sailor.Controller.IsSwimming}" );
			}
			Log.Info( "SHIP MODEL TEST PASSED: walkable cargo hold, broadside cannons, helm and sail controls." );
		}
		catch ( Exception error )
		{
			Log.Error( $"SHIP MODEL TEST FAILED: {error.Message}" );
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"SHIP MODEL TEST OK: {message}" );
	}
}
