using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Checks that joining peers observe one pirate while only the host runs its decisions.</summary>
public static class NpcNetworkSmokeTest
{
	private static string pending;
	private static readonly Dictionary<Guid, string> results = new();

	[ConCmd( "naval_test_npc_network" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || !Networking.IsActive || !Networking.IsHost || pending is not null ) return;
		if ( Connection.All.Count < 2 ) { Log.Warning( "NPC network test needs a host and another client." ); return; }
		var captain = Game.ActiveScene.GetAllComponents<ShipNpc>().SingleOrDefault();
		if ( !captain.IsValid() ) { Log.Error( "NPC NETWORK TEST FAILED: expected one captain." ); return; }
		pending = Guid.NewGuid().ToString();
		results.Clear();
		var connections = Connection.All.Select( x => x.Id ).ToArray();
		try
		{
			NavalNetworkSmokeTest.Run();
			// Give the last host state time to replicate before inspecting each peer's copy.
			int shots = captain.ShotsFired;
			await GameTask.DelaySeconds( 1 );
			Probe( pending, shots );
			for ( int i = 0; i < 40 && !connections.All( results.ContainsKey ); i++ )
				await GameTask.DelaySeconds( 0.2f );
			if ( !connections.All( results.ContainsKey ) ) throw new InvalidOperationException( "A peer did not report its pirate state." );
			foreach ( var id in connections )
			{
				if ( results[id] != "OK" ) throw new InvalidOperationException( $"Peer {id}: {results[id]}" );
			}
			Log.Info( "NPC NETWORK TEST PASSED: all peers see one unowned pirate, replicated target/activity/shots, and clients run no NPC decisions." );
		}
		catch ( Exception e ) { Log.Error( $"NPC NETWORK TEST FAILED: {e.Message}" ); }
		finally { pending = null; results.Clear(); }
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Probe( string token, int minimumShots )
	{
		var captains = Game.ActiveScene.GetAllComponents<ShipNpc>().ToArray();
		string error = "OK";
		if ( captains.Length != 1 ) error = $"Expected one captain, got {captains.Length}";
		else
		{
			var captain = captains[0];
			var sailor = captain.Sailor;
			if ( !sailor.IsValid() || !sailor.IsNpc || !sailor.Ship.IsValid() ) error = "Missing NPC sailor or ship";
			else if ( !sailor.GameObject.Network.Active || !sailor.Ship.GameObject.Network.Active ) error = "Pirate is not networked";
			else if ( captain.Network.Owner is not null || sailor.Ship.Network.Owner is not null ) error = "Pirate has a player owner";
			else if ( captain.IsProxy == Networking.IsHost || sailor.Ship.IsProxy == Networking.IsHost ) error = "Wrong simulation authority";
			else if ( !Networking.IsHost && captain.DecisionTicks != 0 ) error = "Client ran NPC decisions";
			else if ( !captain.Target.IsValid() || string.IsNullOrWhiteSpace( captain.Activity ) || captain.ShotsFired < minimumShots ) error = "Target/activity/shot state did not replicate";
			Log.Info( $"NPC NETWORK VIEW: host={Networking.IsHost}; proxy={captain.IsProxy}; decisions={captain.DecisionTicks}; shots={captain.ShotsFired}; activity={captain.Activity}; result={error}" );
		}
		Report( token, error );
	}

	[Rpc.Host]
	private static void Report( string token, string result )
	{
		if ( !Game.IsEditor || pending != token || Rpc.Caller is null ) return;
		if ( !Connection.All.Any( x => x.Id == Rpc.Caller.Id ) ) return;
		results[Rpc.Caller.Id] = result;
	}
}
