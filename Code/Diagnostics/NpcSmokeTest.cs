using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class NpcSmokeTest
{
	private static bool running;
	[ConCmd( "naval_npc_status" )]
	public static void Status()
	{
		if ( !Game.IsEditor ) return;
		foreach ( var npc in Game.ActiveScene.GetAllComponents<ShipNpc>() )
		{
			if ( !npc.Sailor.IsValid() || !npc.Sailor.Ship.IsValid() ) continue;
			var ship = npc.Sailor.Ship;
			var range = npc.Target.IsValid() ? (npc.Target.WorldPosition - ship.WorldPosition).Length : 0;
			Log.Info( $"NPC: {npc.Activity}; target={npc.Target?.GameObject.Name ?? "none"}; route={npc.NavigationStatus}; visits={npc.StationsVisited}; shots={npc.ShotsFired}; ship={ship.WorldPosition}; sailor={npc.WorldPosition}; sail={ship.Sails.Deployment:0.00}; yaw={ship.WorldRotation.Angles().yaw:0}; range={range:0}; proxy={npc.IsProxy}" );
		}
	}

	[ConCmd( "naval_test_npc" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || !GameplayAuthority.CanMutate || running ) return;
		var npc = Game.ActiveScene?.GetAllComponents<ShipNpc>().FirstOrDefault();
		if ( !npc.IsValid() ) { Log.Error( "NPC TEST FAILED: no captain" ); return; }
		var start = npc.Sailor.Ship.WorldPosition;
		int shots = npc.ShotsFired;
		running = true;
		try
		{
			if ( Networking.IsActive ) NavalNetworkSmokeTest.Run();
			for ( int i = 0; i < 180; i++ )
			{
				await GameTask.DelaySeconds( 1 );
				if ( !npc.IsValid() ) return;
				if ( i % 15 == 0 ) Status();
				if ( npc.ShotsFired > shots && npc.UsedSails && npc.UsedHelm && npc.UsedCannon )
				{
					Log.Info( $"NPC TEST PASSED: autonomous station traversal and firing; ship displacement={(npc.Sailor.Ship.WorldPosition - start).WithZ( 0 ).Length:0}" );
					return;
				}
			}
			Log.Error( "NPC TEST FAILED: captain did not complete station sequence and fire within 180 seconds" );
		}
		finally { running = false; }
	}
}
