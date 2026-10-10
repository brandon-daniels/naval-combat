using System;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Physically walks the local player from the weather deck into the cargo hold and back.</summary>
public static class CargoHoldTraversalSmokeTest
{
	private static bool running;

	[ConCmd( "naval_test_cargo_walk" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || Networking.IsActive || running ) return;
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc );
		if ( !sailor.IsValid() || sailor.Scene.IsEditor ) return;
		running = true;
		var player = sailor.Controller;
		var ship = sailor.Ship;
		bool sailorEnabled = sailor.Enabled;
		bool input = player.UseInputControls;
		bool look = player.UseLookControls;
		float deployment = ship.Sails.Deployment;
		var shipVelocity = ship.Body.IsValid() ? ship.Body.Velocity : Vector3.Zero;
		var shipAngularVelocity = ship.Body.IsValid() ? ship.Body.AngularVelocity : Vector3.Zero;
		try
		{
			ship.Sails.Deployment = 0;
			if ( ship.Body.IsValid() )
			{
				ship.Body.Velocity = Vector3.Zero;
				ship.Body.AngularVelocity = Vector3.Zero;
			}
			sailor.ReturnToDeck();
			// ReturnToDeck normally inherits the moving hull's velocity on the
			// sailor's next fixed update. The diagnostic disables that component,
			// so perform the same handoff before doing so.
			if ( player.Body.IsValid() && ship.Body.IsValid() )
				player.Body.Velocity = ship.Body.GetVelocityAtPoint( player.WorldPosition );
			sailor.Enabled = false;
			player.UseInputControls = false;
			player.UseLookControls = false;
			player.WishVelocity = Vector3.Zero;
			var deckRay = ship.Scene.Trace.Ray( ship.WorldTransform.PointToWorld( new Vector3( -220, -85, 160 ) ), ship.WorldTransform.PointToWorld( new Vector3( -220, -85, -150 ) ) ).IgnoreGameObjectHierarchy( player.GameObject ).Run();
			Log.Info( $"CARGO WALK RAY: hit={deckRay.Hit}, object={deckRay.GameObject}, local={ship.WorldTransform.PointToLocal( deckRay.EndPosition )}, normal={deckRay.Normal}" );
			// Begin on the flat weather deck beside the open hatch. The authored scene
			// spawn sits on the sloped raised roof and is not part of this geometry
			// check; no teleporting occurs once the deck-to-hold route begins.
			player.WorldPosition = ship.WorldTransform.PointToWorld( new Vector3( -220, -85, 100 ) );
			player.Transform.ClearInterpolation();
			if ( player.Body.IsValid() ) player.Body.Velocity = Vector3.Zero;
			await GameTask.DelaySeconds( 0.25f );
			for ( int i = 0; i < 40 && !player.IsOnGround; i++ ) await GameTask.DelaySeconds( 0.1f );
			Check( player.IsOnGround, "Player starts grounded on the weather deck" );
			Log.Info( $"CARGO WALK TEST START: local={ship.WorldTransform.PointToLocal( player.WorldPosition )}; radius={player.BodyRadius}; height={player.BodyHeight}" );

			// Traverse the aft landing, descend the open hatch, then enter the room.
			await WalkTo( new Vector2( -294, -85 ), "reach the aft landing" );
			await WalkTo( new Vector2( -294, 0 ), "line up with the open stair" );
			await WalkTo( new Vector2( -240, 0 ), "enter the cargo stair" );
			await WalkTo( new Vector2( -150, 0 ), "descend the cargo stair" );
			await WalkTo( new Vector2( -50, 0 ), "reach the bottom landing" );
			await WalkTo( new Vector2( 0, 0 ), "walk into the cargo hold" );
			await WalkTo( new Vector2( 0, -55 ), "pass beside the mast support" );
			await WalkTo( new Vector2( 150, -55 ), "walk under the deck into the cargo room" );
			var below = ship.WorldTransform.PointToLocal( player.WorldPosition );
			Check( below.z < 5 && player.IsOnGround && !player.IsSwimming, "Player stands below deck inside the hold" );

			// Walk the same physical route back out; no teleporting is used between
			// the deck and hold assertions.
			await WalkTo( new Vector2( 0, -55 ), "return through the covered cargo room" );
			await WalkTo( new Vector2( 0, 0 ), "line up with the bottom landing" );
			await WalkTo( new Vector2( -50, 0 ), "return to the bottom landing" );
			await WalkTo( new Vector2( -150, 0 ), "climb the cargo stair" );
			await WalkTo( new Vector2( -240, 0 ), "approach the top of the stair" );
			await WalkTo( new Vector2( -294, 0 ), "exit onto the aft landing" );
			await WalkTo( new Vector2( -294, -85 ), "clear the open hatch" );
			var above = ship.WorldTransform.PointToLocal( player.WorldPosition );
			Check( above.z > 20 && player.IsOnGround && !player.IsSwimming, "Player returns smoothly to the top deck" );
			Log.Info( "CARGO WALK TEST PASSED: top deck to hold and back using physical walking only." );
		}
		catch ( Exception error )
		{
			var local = ship.IsValid() && player.IsValid()
				? ship.WorldTransform.PointToLocal( player.WorldPosition )
				: Vector3.Zero;
			Log.Error( $"CARGO WALK TEST FAILED: {error.Message}; local={local}, grounded={player.IsOnGround}, swimming={player.IsSwimming}" );
		}
		finally
		{
			if ( sailor.IsValid() && player.IsValid() )
			{
				player.WishVelocity = Vector3.Zero;
				if ( ship.Body.IsValid() )
				{
					ship.Body.Velocity = shipVelocity;
					ship.Body.AngularVelocity = shipAngularVelocity;
				}
				ship.Sails.Deployment = deployment;
				sailor.ReturnToDeck();
				sailor.Enabled = sailorEnabled;
				player.UseInputControls = input;
				player.UseLookControls = look;
			}
			running = false;
		}

		async Task WalkTo( Vector2 target, string label )
		{
			for ( int i = 0; i < 100; i++ )
			{
				if ( ship.Body.IsValid() )
				{
					ship.Body.Velocity = Vector3.Zero;
					ship.Body.AngularVelocity = Vector3.Zero;
				}
				var local = ship.WorldTransform.PointToLocal( player.WorldPosition );
				var remaining = new Vector2( target.x - local.x, target.y - local.y );
				if ( remaining.Length < 14 )
				{
					player.WishVelocity = Vector3.Zero;
					Log.Info( $"CARGO WALK TEST OK: {label}; local={local}" );
					return;
				}
				var worldTarget = ship.WorldTransform.PointToWorld( new Vector3( target.x, target.y, local.z ) );
				var worldDirection = (worldTarget - player.WorldPosition).WithZ( 0 ).Normal;
				player.WishVelocity = worldDirection * 95;
				await GameTask.DelaySeconds( 0.1f );
				if ( player.IsSwimming ) throw new InvalidOperationException( $"Player fell overboard while trying to {label}" );
			}
			throw new InvalidOperationException( $"Could not {label}" );
		}
	}

	private static void Check( bool value, string message )
	{
		if ( !value ) throw new InvalidOperationException( message );
		Log.Info( $"CARGO WALK TEST OK: {message}" );
	}
}
