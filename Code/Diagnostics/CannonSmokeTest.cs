using System;
using System.Threading.Tasks;
namespace NavalCombat;
public static class CannonSmokeTest
{
	private static bool running;
	[ConCmd( "naval_test_cannons" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		var player = sailor.Controller;
		bool input = player.UseInputControls, look = player.UseLookControls;
		try
		{
			sailor.ReturnToDeck();
			player.UseInputControls = false;
			player.UseLookControls = false;
			var cannons = sailor.Scene.GetAllComponents<ShipCannon>().Where( x => x.Ship == sailor.Ship && x.Enabled ).ToArray();
			Check( cannons.Length == 2, "One cannon on each side exists" );
			foreach ( var cannon in cannons )
			{
				Check( !cannon.Fire( player ), "Unoccupied cannon rejects firing" );
				player.WorldPosition = cannon.SeatPosition.WorldPosition - cannon.WorldRotation.Forward * 25 + Vector3.Up * 70;
				player.WishVelocity = Vector3.Zero;
				player.Body.Velocity = sailor.Ship.Body.GetVelocityAtPoint( player.WorldPosition );
				for ( int i = 0; i < 40 && !player.IsOnGround; i++ ) await GameTask.DelaySeconds( 0.1f );
				Check( player.IsOnGround, "Gunner reaches the physical deck beside " + cannon.GameObject.Name );
				player.WorldPosition = cannon.SeatPosition.WorldPosition - cannon.WorldRotation.Forward * 25 + Vector3.Up * 5;
				player.Body.Velocity = sailor.Ship.Body.GetVelocityAtPoint( player.WorldPosition );
				cannon.TryTake( player );
				if ( cannon.GetOccupant() != player ) Log.Info( $"CANNON TEST: grounded={player.IsOnGround}, distance={(player.WorldPosition - cannon.SeatPosition.WorldPosition).Length:0.0}, occupied={cannon.Occupied}, body={player.Body.Enabled}" );
				Check( cannon.GetOccupant() == player && !player.Body.Enabled, "Gunner mounts " + cannon.GameObject.Name );
				cannon.Aim( player, 1, 1, 0.1f );
				Check( cannon.Yaw > 0 && cannon.Elevation > 12, "Barrel aim responds" );
				Check( cannon.Fire( player ) && !cannon.Fire( player ), "Shot fires and reload blocks repeat" );
				var ball = sailor.Scene.GetAllComponents<Cannonball>().FirstOrDefault( x => x.Source == sailor.Ship );
				Check( ball.IsValid(), "Live ballistic cannonball spawned" );
				await GameTask.DelaySeconds( 4 );
				Check( !ball.IsValid(), "Cannonball impacts before lifetime expiry" );
				cannon.Release( player );
				await GameTask.DelaySeconds( 0.4f );
				Check( player.Body.Enabled && !sailor.ActiveCannon.IsValid(), "Release restores walking" );
			}
			Log.Info( "CANNON TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"CANNON TEST FAILED: {e.Message}" ); }
		finally
		{
			sailor.ReturnToDeck();
			player.UseInputControls = input;
			player.UseLookControls = look;
			running = false;
		}
	}
	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"CANNON TEST OK: {message}" );
	}
}
