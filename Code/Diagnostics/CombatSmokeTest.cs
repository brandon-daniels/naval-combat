using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class CombatSmokeTest
{
	private static bool running;
	[ConCmd( "naval_test_combat" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var scene = Game.ActiveScene;
		var player = scene?.GetAllComponents<SwordFighter>().FirstOrDefault( x => !x.IsNpc );
		var npc = scene?.GetAllComponents<SwordFighter>().FirstOrDefault( x => x.IsNpc );
		if ( !player.IsValid() || !npc.IsValid() || scene.IsEditor ) return;
		running = true;
		npc.NpcAttacks = false;
		player.Controller.UseLookControls = false;
		try
		{
			await GameTask.DelaySeconds( 1 );
			player.WorldPosition = new Vector3( -180, 0, 2 );
			player.Controller.EyeAngles = Angles.Zero;
			Check( player.TrySwing() && npc.Health == 100, "Out of range swing misses" );
			Check( !player.TrySwing(), "Cooldown rejects repeated attack" );
			await GameTask.DelaySeconds( 0.7f );
			player.WorldPosition = npc.WorldPosition + Vector3.Backward * 70;
			player.Controller.EyeAngles = new Angles( 0, 180, 0 );
			Check( player.TrySwing() && npc.Health == 100, "Sword cannot hit behind the attacker" );
			await GameTask.DelaySeconds( 0.7f );
			player.Controller.EyeAngles = Angles.Zero;
			var barrier = new GameObject( scene, true, "Combat test obstruction" );
			barrier.WorldPosition = player.WorldPosition + new Vector3( 35, 0, 42 );
			var collider = barrier.AddComponent<BoxCollider>();
			collider.Scale = new Vector3( 8, 100, 100 );
			try
			{
				await GameTask.DelaySeconds( 0.1f );
				Check( player.TrySwing() && npc.Health == 100, "Wall blocks sword damage" );
			}
			finally { barrier.Destroy(); }
			await GameTask.DelaySeconds( 0.7f );
			Check( player.TrySwing() && npc.Health == 75, "Sword hits NPC for 25 damage" );
			for ( int i = 0; i < 3; i++ )
			{
				await GameTask.DelaySeconds( 0.7f );
				Check( player.TrySwing(), "Follow-up swing accepted" );
			}
			Check( npc.Health == 0 && !npc.TrySwing(), "Defeated NPC cannot attack" );
			await GameTask.DelaySeconds( 3.2f );
			Check( npc.Health == 100, "NPC respawns at full health" );
			player.WorldPosition = npc.WorldPosition + Vector3.Backward * 70;
			npc.NpcAttacks = true;
			await GameTask.DelaySeconds( 1.5f );
			Check( player.Health < 100, "NPC retaliates at close range" );
			Log.Info( "COMBAT TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"COMBAT TEST FAILED: {e.Message}" ); }
		finally
		{
			player.WorldPosition = player.SpawnPosition;
			player.Controller.UseLookControls = true;
			npc.NpcAttacks = true;
			running = false;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"COMBAT TEST OK: {message}" );
	}
}
