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
				Check( !cannon.SetLoading( player, true ), "Unoccupied cannon rejects loading" );
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
				float yaw = cannon.Yaw, elevation = cannon.Elevation;
				float turn = yaw > 30 ? -1 : 1, lift = elevation > 40 ? -1 : 1;
				cannon.Aim( player, turn, lift, 0.1f );
				Check( Math.Abs( cannon.Yaw - yaw - turn * 3.5f ) < 0.01f
					&& Math.Abs( cannon.Elevation - elevation - lift * 2.5f ) < 0.01f, "Barrel aim responds" );
				Check( !cannon.IsLoaded && !cannon.Fire( player ), "Empty cannon cannot fire" );
				Check( cannon.SetLoading( player, true ), "Gunner begins deliberate loading" );
				await GameTask.DelaySeconds( 0.15f );
				Check( cannon.IsLoading && cannon.LoadProgress > 0 && !cannon.Fire( player ), "Loading progresses without allowing an early shot" );
				cannon.SetLoading( player, false );
				Check( !cannon.IsLoading && !cannon.IsLoaded && cannon.LoadProgress == 0, "Releasing load discards unfinished progress" );
				float started = Time.Now;
				for ( int i = 0; i < 120 && !cannon.IsLoaded; i++ )
				{
					cannon.SetLoading( player, true );
					await GameTask.DelaySeconds( 0.1f );
				}
				Check( cannon.IsLoaded && !cannon.IsLoading && Time.Now - started >= Math.Clamp( cannon.ReloadSeconds, 0.5f, 10 ), "Holding load completes the host-timed charge" );
				int sequence = cannon.ShotSequence;
				Check( cannon.Fire( player ) && !cannon.Fire( player ) && cannon.ShotSequence == sequence + 1, "One loaded charge fires exactly once" );
				Check( !cannon.IsLoaded && !cannon.IsLoading, "Shot leaves the cannon empty" );
				var ball = sailor.Scene.GetAllComponents<Cannonball>().FirstOrDefault( x => x.Source == sailor.Ship );
				Check( ball.IsValid(), "Live ballistic cannonball spawned" );
				Check( sailor.Scene.GetAllComponents<CannonBurst>().Any( x => x.GameObject.Name == "Cannon smoke" ), "Shot produces local muzzle smoke" );
				await GameTask.DelaySeconds( Math.Max( 4, Math.Clamp( cannon.ReloadSeconds, 0.5f, 10 ) + 0.2f ) );
				Check( !ball.IsValid(), "Cannonball impacts before lifetime expiry" );
				Check( !cannon.IsLoaded && !cannon.IsLoading && !cannon.Fire( player ), "Waiting after a shot never automatically reloads" );
				cannon.SetLoading( player, true );
				await GameTask.DelaySeconds( 0.1f );
				cannon.Release( player );
				await GameTask.DelaySeconds( 0.4f );
				Check( player.Body.Enabled && !sailor.ActiveCannon.IsValid(), "Release restores walking" );
				Check( !cannon.IsLoading && !cannon.IsLoaded, "Leaving the gun cancels unfinished loading" );
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
