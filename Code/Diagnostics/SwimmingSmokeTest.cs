using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class SwimmingSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_swimming" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		var player = sailor.Controller;
		var ship = sailor.Ship;
		var swim = player.GetComponent<OceanSwimMode>();
		bool input = player.UseInputControls;
		bool look = player.UseLookControls;
		float deployment = ship.Sails.Deployment;
		float trim = ship.Sails.SailAngle;
		float wind = ship.Sails.Wind.Heading;
		try
		{
			player.UseInputControls = false;
			player.UseLookControls = false;
			sailor.ReturnToDeck();
			ship.Sails.Deployment = 1;
			ship.Sails.SailAngle = 0;
			ship.Sails.Wind.Heading = ship.WorldRotation.Angles().yaw;
			await GameTask.DelaySeconds( 2 );
			var local = ship.WorldTransform.PointToLocal( player.WorldPosition );
			for ( int i = 0; i < 60; i++ )
			{
				await GameTask.DelaySeconds( 0.1f );
				Check( (ship.WorldTransform.PointToLocal( player.WorldPosition ) - local).WithZ( 0 ).Length < 20, "Idle deck drift stays below 20 units" );
			}
			Log.Info( "SWIM TEST OK: six seconds stable on accelerating, rolling ship" );
			player.WishVelocity = ship.WorldRotation.Forward.WithZ( 0 ).Normal * 90;
			await GameTask.DelaySeconds( 0.7f );
			player.WishVelocity = Vector3.Zero;
			Check( (ship.WorldTransform.PointToLocal( player.WorldPosition ) - local).WithZ( 0 ).Length > 35, "Walk relative to sailing deck" );
			ship.Sails.Deployment = 0;
			ship.Body.Velocity = Vector3.Zero;
			// Start outside the hull, not underneath its solid bottom.
			var water = ship.WorldTransform.PointToWorld( new Vector3( 0, 180, 0 ) );
			player.WorldPosition = water.WithZ( ship.Ocean.HeightAt( water, Time.Now ) - 45 );
			player.Body.Velocity = Vector3.Zero;
			player.PreventGrounding( 0.2f );
			await GameTask.DelaySeconds( 1 );
			Check( player.IsSwimming, "Overboard enters swimming" );
			var start = player.WorldPosition;
			player.WishVelocity = ship.WorldRotation.Forward.WithZ( 0 ).Normal * 100;
			await GameTask.DelaySeconds( 0.6f );
			player.WishVelocity = Vector3.Zero;
			Check( (player.WorldPosition - start).WithZ( 0 ).Length > 35, "Swimming moves through water" );
			Check( MathF.Abs( ship.Ocean.HeightAt( player.WorldPosition, Time.Now ) - player.WorldPosition.z - swim.FloatDepth ) < 30, "Buoyancy tracks waves" );
			player.EyeAngles = (-ship.WorldRotation.Left).EulerAngles;
			Check( swim.TryWaterJump(), "Surface leap accepted" );
			Check( !swim.TryWaterJump(), "Repeated leap rejected" );
			bool landed = false;
			for ( int i = 0; i < 30; i++ )
			{
				await GameTask.DelaySeconds( 0.1f );
				if ( player.IsOnGround ) { landed = true; break; }
			}
			Check( landed, "Water jump lands aboard" );
			Log.Info( "SWIM TEST PASSED: deck drift, walk, waves, swim and boarding." );
		}
		catch ( Exception error ) { Log.Error( $"SWIM TEST FAILED: {error.Message}" ); }
		finally
		{
			if ( sailor.IsValid() && player.IsValid() )
			{
				ship.Sails.Deployment = deployment;
				ship.Sails.SailAngle = trim;
				ship.Sails.Wind.Heading = wind;
				sailor.ReturnToDeck();
				player.UseInputControls = input;
				player.UseLookControls = look;
			}
			running = false;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
	}
}
