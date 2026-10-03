using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class CombatNetworkSmokeTest
{
	private static bool running;
	[ConCmd( "naval_test_combat_network" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || !Networking.IsHost || !Networking.IsActive || running ) return;
		var fighters = Game.ActiveScene.GetAllComponents<SwordFighter>().Where( x => !x.IsNpc ).ToArray();
		if ( fighters.Length != 2 ) { Log.Warning( "Combat network test requires exactly two players." ); return; }
		running = true;
		var host = fighters.First( x => x.Network.Owner == Connection.Local );
		var client = fighters.First( x => x != host );
		try
		{
			host.Health = 100;
			client.Health = 100;
			Stage( 0 );
			await GameTask.DelaySeconds( 1 );
			int before = client.Health;
			Stage( 1 );
			await GameTask.DelaySeconds( 1 );
			Check( client.Health == before - 25, "Host sword damages remote player" );
			before = host.Health;
			Stage( 2 );
			await GameTask.DelaySeconds( 1 );
			Check( host.Health == before - 25, "Client sword RPC damages host" );
			for ( int i = 0; i < 3; i++ )
			{
				Stage( 1 );
				await GameTask.DelaySeconds( 0.8f );
			}
			Check( client.Health == 0, "Remote player defeated" );
			Stage( 2 );
			await GameTask.DelaySeconds( 0.3f );
			Check( host.Health == 75, "Defeated client cannot deal damage" );
			await GameTask.DelaySeconds( 3 );
			Check( client.Health == 100 && (client.WorldPosition - client.SpawnPosition).Length < 15, "Remote player respawns at own spawn" );
			Log.Info( "COMBAT NETWORK TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"COMBAT NETWORK TEST FAILED: {e.Message}" ); }
		finally { Stage( 3 ); running = false; }
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Stage( int stage )
	{
		var scene = Game.ActiveScene;
		var local = scene?.GetAllComponents<SwordFighter>().FirstOrDefault( x => x.IsLocalPlayer );
		if ( !local.IsValid() ) return;
		if ( stage == 0 )
		{
			local.Controller.UseLookControls = false;
			local.WorldPosition = new Vector3( Networking.IsHost ? -70 : 0, -300, 2 );
			local.Controller.EyeAngles = new Angles( 0, Networking.IsHost ? 0 : 180, 0 );
			local.Controller.Body.Velocity = Vector3.Zero;
		}
		if ( stage == 1 && Networking.IsHost || stage == 2 && !Networking.IsHost ) local.Swing();
		if ( stage == 3 )
		{
			local.WorldPosition = local.SpawnPosition;
			local.Controller.UseLookControls = true;
		}
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"COMBAT NETWORK TEST OK: {message}" );
	}
}
