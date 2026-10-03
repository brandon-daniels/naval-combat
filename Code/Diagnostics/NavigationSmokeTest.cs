using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class NavigationSmokeTest
{
	private static bool running;
	private static Scene testScene;

	[ConCmd( "naval_test_routes" )]
	public static void Routes()
	{
		if ( !Game.IsEditor ) return;
		try
		{
			var route = new SailingRoute();
			Check( route.Plan( Vector3.Zero, new Vector3( 3000, 0, 0 ), 0 ) && route.Points.Count == 1, "Clear downwind route is direct" );
			Check( route.Plan( Vector3.Zero, new Vector3( -3000, 0, 0 ), 0 ) && route.Points.Count >= 2, "Direct upwind goal produces tacks" );
			Validate( route, Vector3.Zero, 0 );
			route.Islands.Add( new SailingRoute.Obstacle( new Vector3( 2500, 0, 0 ), 1000 ) );
			Check( route.Plan( Vector3.Zero, new Vector3( 5000, 0, 0 ), 0 ) && route.Points.Count > 1, "Island blocks direct route and produces detour" );
			Validate( route, Vector3.Zero, 0 );
			Check( route.Plan( new Vector3( 5000, 0, 0 ), Vector3.Zero, 0 ), "Combined island and upwind route exists" );
			Validate( route, new Vector3( 5000, 0, 0 ), 0 );
			Check( route.Plan( Vector3.Zero, new Vector3( 5000, 0, 0 ), 180 ), "Changed wind rebuilds the route" );
			Validate( route, Vector3.Zero, 180 );
			Check( !route.Plan( Vector3.Zero, new Vector3( 2500, 0, 0 ), 0 ), "Unsafe destination rejected" );
			Check( SailRig.HeadingPower( -1 ) == 0 && SailRig.HeadingPower( -0.8f ) == 0 && SailRig.HeadingPower( -0.5f ) > 0, "No-go zone and close-hauled drive" );
			route.ReadIslands( Game.ActiveScene, 700 );
			Check( route.Islands.Count >= 3, "Authored shorelines discovered" );
			Check( route.Plan( new Vector3( 0, -1500, 0 ), new Vector3( 5200, -1500, 0 ), 0 ), "Route around authored Beacon Island" );
			Validate( route, new Vector3( 0, -1500, 0 ), 0 );
			Log.Info( "ROUTE TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"ROUTE TEST FAILED: {e.Message}" ); }
	}

	private static void Validate( SailingRoute route, Vector3 start, float wind )
	{
		foreach ( var point in route.Points )
		{
			Check( route.Clear( start, point ), "Route leg clears shoreline margin" );
			Check( Vector3.Dot( (point - start).Normal, Rotation.FromYaw( wind ).Forward ) >= -0.501f, "Route leg stays outside no-go zone" );
			start = point;
		}
	}

	[ConCmd( "naval_test_navigation" )]
	public static async Task Navigate( bool upwind = false )
	{
		if ( !Game.IsEditor || Networking.IsActive || (running && testScene == Game.ActiveScene) ) return;
		var npc = Game.ActiveScene?.GetAllComponents<ShipNpc>().FirstOrDefault();
		if ( !npc.IsValid() || !npc.Target.IsValid() ) return;
		running = true;
		testScene = Game.ActiveScene;
		var ship = npc.Sailor.Ship;
		var target = npc.Target;
		var wind = ship.Sails.Wind;
		var originalShip = ship.WorldTransform;
		var originalTarget = target.WorldTransform;
		float heading = wind.Heading, strength = wind.Strength, deployment = target.Sails.Deployment, range = npc.EngagementRange;
		var human = Game.ActiveScene.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		bool humanEnabled = human.IsValid() && human.Enabled;
		bool humanInput = human.IsValid() && human.Controller.UseInputControls;
		bool humanLook = human.IsValid() && human.Controller.UseLookControls;
		try
		{
			npc.Enabled = false;
			if ( human.IsValid() )
			{
				human.ReturnToDeck();
				human.Enabled = false;
				human.Controller.UseInputControls = false;
				human.Controller.UseLookControls = false;
			}
			npc.EngagementRange = upwind ? 1500 : 1800;
			wind.Heading = 0;
			wind.Strength = 1;
			ship.Sails.Deployment = 0;
			target.Sails.Deployment = 0;
			ship.WorldPosition = upwind ? new Vector3( -4000, -6000, 30 ) : new Vector3( 0, -1500, 30 );
			target.WorldPosition = upwind ? new Vector3( -6800, -6000, 30 ) : new Vector3( 5200, -1500, 30 );
			ship.WorldRotation = Rotation.Identity;
			ship.Body.Velocity = Vector3.Zero;
			ship.Body.AngularVelocity = Vector3.Zero;
			target.Body.Velocity = Vector3.Zero;
			npc.Sailor.ReturnToDeck();
			human?.ReturnToDeck();
			await GameTask.DelaySeconds( 1 );
			int shots = npc.ShotsFired, tacks = npc.TacksCompleted;
			var safety = new SailingRoute();
			safety.ReadIslands( npc.Scene, 290 );
			npc.Enabled = true;
			for ( int i = 0; i < 180; i++ )
			{
				await GameTask.DelaySeconds( 1 );
				if ( !npc.IsValid() ) return;
				CheckQuiet( safety.Clear( ship.WorldPosition, ship.WorldPosition ), "Hull entered shoreline envelope" );
				if ( i % 15 == 0 ) NpcSmokeTest.Status();
				if ( npc.ShotsFired <= shots ) continue;
				CheckQuiet( !upwind || npc.TacksCompleted > tacks, "No upwind tack completed" );
				Log.Info( $"NAVIGATION TEST PASSED: {(upwind ? "upwind tacking" : "island avoidance")} and cannon engagement; {i + 1} seconds" );
				return;
			}
			throw new InvalidOperationException( "Did not reach firing position in 180 seconds" );
		}
		catch ( Exception e ) { Log.Error( $"NAVIGATION TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( npc.IsValid() && ship.IsValid() && target.IsValid() )
			{
				npc.Enabled = false;
				npc.EngagementRange = range;
				ship.WorldTransform = originalShip;
				target.WorldTransform = originalTarget;
				ship.Body.Velocity = Vector3.Zero;
				ship.Body.AngularVelocity = Vector3.Zero;
				target.Body.Velocity = Vector3.Zero;
				wind.Heading = heading;
				wind.Strength = strength;
				target.Sails.Deployment = deployment;
				npc.Sailor.ReturnToDeck();
				human?.ReturnToDeck();
				if ( human.IsValid() )
				{
					human.Enabled = humanEnabled;
					human.Controller.UseInputControls = humanInput;
					human.Controller.UseLookControls = humanLook;
				}
				npc.Enabled = true;
			}
			running = false;
		}
	}

	private static void CheckQuiet( bool value, string message )
	{
		if ( !value ) throw new InvalidOperationException( message );
	}
	private static void Check( bool value, string message )
	{
		CheckQuiet( value, message );
		Log.Info( "ROUTE TEST OK: " + message );
	}
}

