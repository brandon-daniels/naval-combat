using System;
using System.Threading.Tasks;
namespace NavalCombat;
public static class SailingSmokeTest
{
	private static bool running;
	[ConCmd( "naval_test_sails" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		var rig = sailor.Sails;
		var player = sailor.Controller;
		float deployment = rig.Deployment, angle = rig.SailAngle, heading = rig.Wind.Heading, strength = rig.Wind.Strength;
		bool input = player.UseInputControls, look = player.UseLookControls;
		try
		{
			player.UseInputControls = false;
			player.UseLookControls = false;
			rig.Deployment = 0;
			sailor.ReturnToDeck();
			await GameTask.DelaySeconds( 1 );
			rig.Adjust( player, 1, 1, 0.1f );
			Check( rig.Deployment == 0, "Remote sail controls rejected" );
			player.WorldPosition = rig.SeatPosition.WorldPosition - rig.WorldRotation.Forward * 25 + Vector3.Up * 70;
			player.WishVelocity = Vector3.Zero;
			player.Body.Velocity = rig.Ship.Body.GetVelocityAtPoint( player.WorldPosition );
			for ( int i = 0; i < 40 && !sailor.IsAtMast; i++ )
			{
				await GameTask.DelaySeconds( 0.1f );
				if ( player.IsOnGround )
				{
					player.WorldPosition = rig.SeatPosition.WorldPosition - rig.WorldRotation.Forward * 25 + Vector3.Up * 5;
					player.Body.Velocity = rig.Ship.Body.GetVelocityAtPoint( player.WorldPosition );
				}
				rig.TryTake( player );
			}
			if ( !sailor.IsAtMast ) Log.Info( $"SAIL TEST: grounded={player.IsOnGround}, distance={(player.WorldPosition - rig.SeatPosition.WorldPosition).Length:0.0}, occupied={rig.Occupied}, body={player.Body.Enabled}" );
			Check( sailor.IsAtMast, "Mast reachable from deck" );
			await GameTask.DelaySeconds( 0.3f );
			Check( sailor.IsAtMast && !player.Body.Enabled, "Mast attaches player" );
			float before = rig.SailAngle;
			for ( int i = 0; i < 30; i++ ) rig.Adjust( player, 1, 1, 0.1f );
			Check( rig.Deployment == 1 && rig.SailAngle != before, "Lower and rotate controls work" );
			rig.Wind.Heading = rig.Ship.WorldRotation.Angles().yaw;
			rig.SailAngle = 0;
			Check( rig.DriveFraction > 0.8f, "Aligned full sail catches wind" );
			rig.SailAngle = 180;
			Check( rig.DriveFraction == 0, "Back-facing sail loses drive" );
			rig.SailAngle = 0;
			rig.Wind.Strength = 0;
			Check( rig.DriveFraction == 0, "Calm wind gives no drive" );
			rig.Wind.Strength = 1;
			rig.Release( player );
			await GameTask.DelaySeconds( 0.5f );
			Check( !sailor.IsAtMast && player.Body.Enabled, "Mast release restores walking" );
			var start = rig.Ship.WorldPosition;
			await GameTask.DelaySeconds( 2 );
			Check( (rig.Ship.WorldPosition - start).WithZ( 0 ).Length > 100, "Unattended sail propels ship" );
			sailor.ReturnToDeck();
			player.WorldPosition = rig.SeatPosition.WorldPosition - rig.WorldRotation.Forward * 25 + Vector3.Up * 70;
			player.WishVelocity = Vector3.Zero;
			player.Body.Velocity = rig.Ship.Body.GetVelocityAtPoint( player.WorldPosition );
			for ( int i = 0; i < 40 && !sailor.IsAtMast; i++ )
			{
				await GameTask.DelaySeconds( 0.1f );
				if ( player.IsOnGround )
				{
					player.WorldPosition = rig.SeatPosition.WorldPosition - rig.WorldRotation.Forward * 25 + Vector3.Up * 5;
					player.Body.Velocity = rig.Ship.Body.GetVelocityAtPoint( player.WorldPosition );
				}
				rig.TryTake( player );
			}
			Check( sailor.IsAtMast, "Mast can be retaken while sailing" );
			for ( int i = 0; i < 30; i++ ) rig.Adjust( player, 0, -1, 0.1f );
			Check( rig.Deployment == 0 && rig.DriveFraction == 0, "Raising sail removes propulsion" );
			Log.Info( "SAIL TEST PASSED" );
		}
		catch ( Exception error ) { Log.Error( $"SAIL TEST FAILED: {error.Message}" ); }
		finally
		{
			if ( sailor.IsValid() )
			{
				rig.Deployment = deployment;
				rig.SailAngle = angle;
				rig.Wind.Heading = heading;
				rig.Wind.Strength = strength;
				sailor.ReturnToDeck();
				player.UseInputControls = input;
				player.UseLookControls = look;
			}
			running = false;
		}
	}
	private static void Check( bool value, string message )
	{
		if ( !value ) throw new InvalidOperationException( message );
		Log.Info( $"SAIL TEST OK: {message}" );
	}
}
