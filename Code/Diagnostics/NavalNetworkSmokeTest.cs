using System;

namespace NavalCombat;

/// <summary>Host-side structural checks for the multiplayer sailing session.</summary>
public static class NavalNetworkSmokeTest
{
	[ConCmd( "naval_test_network_shell" )]
	public static void Run()
	{
		if ( !Game.IsEditor || !Networking.IsActive || !Networking.IsHost )
		{
			Log.Warning( "Naval network shell test requires an editor-hosted network session." );
			return;
		}

		var scene = Game.ActiveScene;
		var session = scene.GetAllComponents<NavalMultiplayerSession>().SingleOrDefault();
		Check( session.IsValid(), "Exactly one multiplayer session is active" );

		var players = scene.GetAllComponents<ShipPlayer>().Where( x => !x.IsNpc ).ToArray();
		var connections = Connection.All.ToArray();
		Check( players.Length == connections.Length, "One sailor exists per connection" );

		foreach ( var connection in connections )
		{
			var ownedPlayers = players.Where( x => x.Network.Owner == connection ).ToArray();
			Check( ownedPlayers.Length == 1, $"{connection.DisplayName} owns exactly one sailor" );
			var player = ownedPlayers[0];
			Check( player.Ship.IsValid(), $"{connection.DisplayName}'s sailor references a ship" );
			Check( player.Ship.Network.Owner == connection, $"{connection.DisplayName} owns the referenced ship" );
			Check( player.GameObject.Network.Active && player.Ship.GameObject.Network.Active, $"{connection.DisplayName}'s sailor and ship are networked" );
		}

		Check( players.Select( x => x.Ship ).Distinct().Count() == players.Length, "No players share an assigned ship" );
		var captains = scene.GetAllComponents<ShipNpc>().ToArray();
		foreach ( var captain in captains )
		{
			Check( captain.Sailor.IsValid() && captain.Sailor.IsNpc && captain.Sailor.Ship.IsValid(), "Pirate captain has its own NPC sailor and ship" );
			Check( captain.Network.Owner is null && captain.Sailor.Ship.Network.Owner is null, "Pirate is simulated by the host, not a player owner" );
			Check( captain.GameObject.Network.Active && captain.Sailor.Ship.GameObject.Network.Active, "Pirate sailor and ship are networked" );
		}
		Check( captains.Length == (session.SpawnNpcShip ? 1 : 0), "Configured pirate count matches the session" );
		var assignedShips = players.Select( x => x.Ship ).Concat( captains.Select( x => x.Sailor.Ship ) ).ToArray();
		var ships = scene.GetAllComponents<ArcadeShip>().Where( x => x.GameObject.Network.Active ).ToArray();
		Check( assignedShips.Distinct().Count() == assignedShips.Length && ships.Length == assignedShips.Length && ships.All( assignedShips.Contains ),
			"No orphaned network ships remain" );
		var identities = players.Select( x => x.GetComponent<PlayerVoyage>()?.Identity ).ToArray();
		Check( identities.All( x => !string.IsNullOrWhiteSpace( x ) ) && identities.Distinct().Count() == players.Length,
			"Every sailor has a distinct voyage identity" );
		foreach ( var barrel in scene.GetAllComponents<GoodsBarrel>() )
		{
			Check( identities.Contains( barrel.OwnerId ), "Produced cargo belongs to a current voyage" );
		}
		foreach ( var station in scene.GetAllComponents<ShipStation>() )
		{
			Check( station.Ship.IsValid(), $"{station.GameObject.Name} has a ship authority boundary" );
			if ( station.Occupied ) Check( station.Occupant.Network.Owner == station.Ship.Network.Owner
				&& (station.Occupant.Network.Owner is not null || captains.Any( x => x.Sailor.Controller == station.Occupant && x.Sailor.Ship == station.Ship )),
				$"{station.GameObject.Name} has an authorized human or NPC occupant" );
		}

		Log.Info( "NAVAL NETWORK SHELL TEST PASSED" );
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"NAVAL NETWORK SHELL TEST OK: {message}" );
	}
}
