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
		var ships = scene.GetAllComponents<ArcadeShip>().Where( x => x.GameObject.Network.Active ).ToArray();
		Check( ships.Length == players.Length && ships.All( ship => players.Any( player => player.Ship == ship ) ),
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
			if ( station.Occupied ) Check( station.Occupant.Network.Owner is not null, $"{station.GameObject.Name} has a network-owned occupant" );
		}

		Log.Info( "NAVAL NETWORK SHELL TEST PASSED" );
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"NAVAL NETWORK SHELL TEST OK: {message}" );
	}
}
