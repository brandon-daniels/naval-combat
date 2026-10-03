using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class NpcSmokeTest
{
	[ConCmd( "naval_npc_status" )]
	public static void Status()
	{
		if ( !Game.IsEditor ) return;
		foreach ( var npc in Game.ActiveScene.GetAllComponents<ShipNpc>() )
		{
			var ship = npc.Sailor.Ship;
			Log.Info( $"NPC TARGET: {npc.Target.WorldPosition}; sail={npc.Target.Sails.Deployment:0.00}; speed={npc.Target.Body.Velocity}; separateRig={ship.Sails != npc.Target.Sails}" );
			Log.Info( $"NPC: {npc.Activity}; route={npc.NavigationStatus}; waypoints={npc.WaypointsRemaining}; tacks={npc.TacksCompleted}; visits={npc.StationsVisited}; shots={npc.ShotsFired}; ship={ship.WorldPosition}; sailor={npc.WorldPosition}; sail={ship.Sails.Deployment:0.00}; yaw={ship.WorldRotation.Angles().yaw:0}; range={(npc.Target.WorldPosition - ship.WorldPosition).Length:0}" );
		}
	}

	[ConCmd( "naval_test_npc" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive ) return;
		var npc = Game.ActiveScene?.GetAllComponents<ShipNpc>().FirstOrDefault();
		if ( !npc.IsValid() ) { Log.Error( "NPC TEST FAILED: no captain" ); return; }
		var start = npc.Sailor.Ship.WorldPosition;
		int shots = npc.ShotsFired;
		for ( int i = 0; i < 120; i++ )
		{
			await GameTask.DelaySeconds( 1 );
			if ( !npc.IsValid() ) return;
			if ( i % 10 == 0 ) Status();
			if ( npc.ShotsFired > shots && npc.UsedSails && npc.UsedHelm && npc.UsedCannon )
			{
				Log.Info( $"NPC TEST PASSED: autonomous station traversal and firing; ship displacement={(npc.Sailor.Ship.WorldPosition - start).WithZ( 0 ).Length:0}" );
				return;
			}
		}
		Log.Error( "NPC TEST FAILED: captain did not complete station sequence and fire within 120 seconds" );
	}
}
