using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class NpcCrewSmokeTest
{
	[ConCmd( "naval_test_npc_crew" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive ) return;
		var scene = Game.ActiveScene;
		var crew = scene?.GetAllComponents<NpcShipCrew>().FirstOrDefault();
		if ( !crew.IsValid() || scene.IsEditor ) return;
		float range = crew.EngagementRange;
		var target = crew.Target;
		bool acquire = crew.AutoAcquireTarget;
		try
		{
			await GameTask.DelaySeconds( 0.5f );
			Check( scene.GetAllComponents<ArcadeShip>().Count() == 2, "Exactly two saved ships, no runtime clone" );
			Check( scene.GetAllComponents<ShipNpc>().Count() == 1, "One captain spawned" );
			crew.EngagementRange = 2468;
			crew.Target = null;
			crew.AutoAcquireTarget = false;
			await GameTask.DelaySeconds( 0.3f );
			var npc = scene.GetAllComponents<ShipNpc>().First();
			Check( npc.EngagementRange == 2468 && !npc.Target.IsValid(), "Inspector settings update the captain" );
			Check( npc.Activity == "Waiting for another ship", "Target acquisition can be disabled" );
			crew.Enabled = false;
			await GameTask.DelaySeconds( 0.3f );
			Check( !scene.GetAllComponents<ShipNpc>().Any(), "Disabling crew removes captain" );
			Check( scene.GetAllComponents<ShipStation>().Where( x => x.Ship == crew.Ship ).All( x => !x.IsOccupied ), "Stations released during cleanup" );
			crew.Enabled = true;
			await GameTask.DelaySeconds( 0.3f );
			Check( scene.GetAllComponents<ShipNpc>().Count() == 1, "Re-enable creates exactly one captain" );
			Log.Info( "NPC CREW TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"NPC CREW TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( crew.IsValid() )
			{
				crew.EngagementRange = range;
				crew.Target = target;
				crew.AutoAcquireTarget = acquire;
				crew.Enabled = true;
			}
		}
	}
	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( "NPC CREW TEST OK: " + message );
	}
}
