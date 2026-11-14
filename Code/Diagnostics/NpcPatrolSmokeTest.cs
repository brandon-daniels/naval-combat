using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Live local check of patrol motion, target acquisition and bounded pursuit.</summary>
public static class NpcPatrolSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_npc_patrol" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var npc = Game.ActiveScene?.GetAllComponents<ShipNpc>().FirstOrDefault();
		if ( !npc.IsValid() || !npc.Sailor.IsValid() || !npc.Sailor.Ship.IsValid() ) return;
		var ship = npc.Sailor.Ship;
		float detection = npc.DetectionRange, disengage = npc.DisengageRange;
		float duration = npc.MaximumPursuitSeconds, delay = npc.ReengageDelay;
		running = true;
		try
		{
			// Suppress discovery and expire any existing chase without moving either ship.
			npc.DetectionRange = 0;
			npc.MaximumPursuitSeconds = 1;
			npc.ReengageDelay = 1;
			await GameTask.DelaySeconds( 3 );
			Check( !npc.Target.IsValid() && npc.Behaviour == "Patrolling", $"No nearby target returns captain to patrol (state={npc.Behaviour}, target={npc.Target?.GameObject.Name}, ticks={npc.DecisionTicks})" );
			var start = ship.WorldPosition;
			for ( int i = 0; i < 60; i++ )
			{
				await GameTask.DelaySeconds( 1 );
				if ( !npc.IsValid() ) return;
				if ( (ship.WorldPosition - start).WithZ( 0 ).Length > 150 && ship.Sails.Deployment > 0.1f ) break;
			}
			Check( (ship.WorldPosition - start).WithZ( 0 ).Length > 150 && ship.Sails.Deployment > 0.1f,
				"Captain physically uses stations and sails on patrol" );
			npc.DetectionRange = 10000;
			npc.DisengageRange = 12000;
			npc.MaximumPursuitSeconds = 3;
			npc.ReengageDelay = 5;
			await GameTask.DelaySeconds( 0.5f );
			Check( npc.Target.IsValid(), "Captain discovers a nearby player ship" );
			await GameTask.DelaySeconds( 3.5f );
			Check( !npc.Target.IsValid() && npc.Behaviour == "Disengaging", "Pursuit timeout releases target" );
			int shots = npc.ShotsFired;
			await GameTask.DelaySeconds( 2 );
			Check( !npc.Target.IsValid() && npc.ShotsFired == shots, "Cooldown prevents immediate reacquisition and cannon fire" );
			npc.MaximumPursuitSeconds = 120;
			await GameTask.DelaySeconds( 4 );
			Check( npc.Target.IsValid(), "Discovery resumes after cooldown" );
			npc.DetectionRange = 1;
			npc.DisengageRange = 1;
			await GameTask.DelaySeconds( 0.5f );
			Check( !npc.Target.IsValid() && npc.Behaviour == "Disengaging", "Escaping detection range breaks pursuit" );
			Log.Info( "NPC PATROL TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"NPC PATROL TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( npc.IsValid() )
			{
				npc.DetectionRange = detection;
				npc.DisengageRange = disengage;
				npc.MaximumPursuitSeconds = duration;
				npc.ReengageDelay = delay;
			}
			running = false;
		}
	}

	[ConCmd( "naval_test_npc_recovery" )]
	public static async Task Recovery()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var npc = Game.ActiveScene?.GetAllComponents<ShipNpc>().FirstOrDefault();
		if ( !npc.IsValid() || !npc.Sailor.IsValid() || !npc.Sailor.Ship.IsValid() ) return;
		var ship = npc.Sailor.Ship;
		var player = npc.Sailor.Controller;
		bool controllerEnabled = player.Enabled, motion = player.Body.MotionEnabled, shipMotion = ship.Body.MotionEnabled;
		bool enabled = npc.Enabled;
		int recoveries = npc.Recoveries;
		running = true;
		try
		{
			npc.Enabled = false;
			// Immobilize the captain away from every station to simulate a blocked deck route.
			player.Enabled = false;
			player.Body.MotionEnabled = false;
			ship.Body.MotionEnabled = false;
			npc.WorldPosition = ship.WorldPosition + ship.WorldRotation * new Vector3( 0, 0, 300 );
			npc.Enabled = true;
			await GameTask.DelaySeconds( 11 );
			Check( npc.Recoveries > recoveries, "Blocked captain returns to the deck instead of retrying forever" );
		}
		catch ( Exception e ) { Log.Error( $"NPC RECOVERY TEST FAILED: {e.Message}" ); return; }
		finally
		{
			if ( npc.IsValid() && player.IsValid() && ship.IsValid() )
			{
				npc.Enabled = false;
				player.Enabled = controllerEnabled;
				player.Body.MotionEnabled = motion;
				ship.Body.MotionEnabled = shipMotion;
				npc.Sailor.ReturnToDeck();
				npc.Enabled = enabled;
			}
			running = false;
		}
		int visits = npc.StationsVisited;
		for ( int i = 0; i < 30; i++ )
		{
			await GameTask.DelaySeconds( 1 );
			if ( !npc.IsValid() ) return;
			if ( npc.StationsVisited <= visits ) continue;
			Log.Info( "NPC RECOVERY TEST PASSED: blocked captain recovered and resumed physical station use" );
			return;
		}
		Log.Error( "NPC RECOVERY TEST FAILED: captain did not resume station use" );
	}

	private static void Check( bool value, string message )
	{
		if ( !value ) throw new InvalidOperationException( message );
		Log.Info( "NPC PATROL TEST OK: " + message );
	}
}
