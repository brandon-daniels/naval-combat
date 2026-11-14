using System;
using System.Threading.Tasks;

namespace NavalCombat;

public static class CombatNetworkSmokeTest
{
	private enum Stage { Prepare, Place, DrawTools, CheckTools, DrawSword, HostLunge, ClientLunge, AnswerBoth, WrongClient, Escape, Separate, HostAnswer, Restore }

	private static readonly Vector3 PlatformTop = new( 0, -300, 1400 );
	private static bool running;
	private static Guid reportedDuel;
	private static int reportedBeat = -1;
	private static bool clientSawTools;
	private static GameObject platform;
	private static SwordFighter prepared;
	private static ArcadeShip originalDeck;
	private static Transform originalTransform;
	private static Angles originalAim;
	private static Vector3 originalVelocity;
	private static bool originalLook;
	private static bool originalInput;

	[ConCmd( "naval_test_combat_network" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || !Networking.IsHost || !Networking.IsActive || running ) return;
		var fighters = Game.ActiveScene.GetAllComponents<SwordFighter>().Where( x => !x.IsNpc ).ToArray();
		if ( fighters.Length != 2 || fighters.Any( x => !x.CanDuel || x.InDuel ) )
		{
			Log.Warning( "Combat network test requires two living players on foot." );
			return;
		}
		var host = fighters.Single( x => x.Network.Owner == Connection.Local );
		var client = fighters.Single( x => x != host );
		var originalHealth = (host.Health, client.Health);
		var originalItems = (host.EquippedItem, client.EquippedItem);
		int damage = Math.Clamp( host.Damage, 1, 100 );
		running = true;
		try
		{
			host.Health = client.Health = 100;
			Issue( Stage.Prepare );
			await WaitFor( () => Staged( host, client ), "Both owners stand on the isolated test platform" );
			clientSawTools = false;
			Issue( Stage.DrawTools );
			await WaitFor( () => host.EquippedItem == HeldItem.Planks && client.EquippedItem == HeldItem.Bucket,
				"Independent host and remote selections reach the authority" );
			Issue( Stage.CheckTools );
			await WaitFor( () => clientSawTools, "Remote owner sees both replicated tools and cannot change the other owner's selection" );
			Check( !host.TrySwing() && !client.TrySwing(), "Neither tool can start a sword attack on the host" );
			Issue( Stage.DrawSword );
			Issue( Stage.HostLunge );
			await WaitFor( () => Paired( host, client ), "Host lunge reserves one pair" );
			Check( client.EquippedItem == HeldItem.Sword && !client.TryEquip( HeldItem.Planks ), "Challenged defender draws their sword and cannot switch in a duel" );
			Check( host.Health == 100 && client.Health == 100, "Lunge causes no immediate PvP damage" );
			await PlayBeat( host, Stage.AnswerBoth );
			Check( host.Health == 100 && client.Health == 100, "Both owners parry the same note" );
			await PlayBeat( host, Stage.WrongClient );
			Check( reportedDuel == host.DuelId && reportedBeat == host.BeatIndex - 1 && host.Health == 100 && client.Health == 100 - damage,
				"Remote owner pressed the wrong key then the correct key and took exactly one sword hit" );
			Issue( Stage.Escape );
			await WaitFor( () => !host.InDuel && !client.InDuel, "Crouch request from client frees both owners" );
			float escaped = Time.Now;
			Issue( Stage.HostLunge );
			await GameTask.DelaySeconds( 0.35f );
			Check( !host.InDuel && !client.InDuel, "Fresh escape prevents immediate recapture" );

			Issue( Stage.Place );
			await WaitFor( () => Staged( host, client ), "Both owners are back in lunge range" );
			await GameTask.DelaySeconds( Math.Max( 0.01f, 2.2f - (Time.Now - escaped) ) );
			Issue( Stage.HostLunge );
			await WaitFor( () => Paired( host, client ), "A fresh duel can start after escape protection" );
			var separationHealth = (host.Health, client.Health);
			Issue( Stage.Separate );
			await WaitFor( () => !host.InDuel && !client.InDuel, "Moving the remote owner out of reach releases both players" );
			Check( host.Health == separationHealth.Item1 && client.Health == separationHealth.Item2,
				"Separation does not charge either owner for an unanswered note" );
			Check( host.Feedback == "Opponent out of reach" && client.Feedback == host.Feedback,
				"Both owners receive separation feedback" );
			float separated = Time.Now;
			Issue( Stage.Place );
			await WaitFor( () => Staged( host, client ), "Both owners return from the separation fixture" );
			await GameTask.DelaySeconds( Math.Max( 0.01f, 2.2f - (Time.Now - separated) ) );
			Issue( Stage.ClientLunge );
			await WaitFor( () => Paired( host, client ), "Remote owner lunge starts a fresh duel" );
			int maxBeats = (client.Health + damage - 1) / damage;
			for ( int i = 0; i < maxBeats; i++ )
			{
				int before = client.Health;
				await PlayBeat( host, Stage.HostAnswer );
				Check( client.Health == Math.Max( 0, before - damage ) && host.Health == 100, "An unanswered note damages only its owner" );
			}
			Check( client.Health == 0 && !host.InDuel && !client.InDuel, "Defeat releases both players" );
			Issue( Stage.ClientLunge );
			await GameTask.DelaySeconds( 0.25f );
			Check( !host.InDuel && host.Health == 100 && client.Health == 0, "Defeated client cannot initiate an attack" );
			await WaitFor( () => client.Health == 100 && client.WorldPosition.Distance( RespawnPoint( client ) ) < 100,
				"Remote player respawns at their own spawn" );
			Log.Info( "COMBAT NETWORK TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"COMBAT NETWORK TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( host.IsValid() && host.InDuel ) host.FleeDuel();
			if ( host.IsValid() ) host.Health = originalHealth.Item1;
			if ( client.IsValid() ) client.Health = originalHealth.Item2;
			if ( host.IsValid() ) host.TryEquip( originalItems.Item1 );
			if ( client.IsValid() ) client.TryEquip( originalItems.Item2 );
			Issue( Stage.Restore );
			running = false;
		}
	}

	private static async Task PlayBeat( SwordFighter fighter, Stage stage )
	{
		int beat = fighter.BeatIndex;
		float delay = fighter.BeatAt - Time.Now - 0.025f;
		if ( delay > 0 ) await GameTask.DelaySeconds( delay );
		Issue( stage, beat );
		await WaitFor( () => !fighter.InDuel || fighter.BeatIndex > beat, $"Host resolves beat {beat + 1}" );
	}

	private static bool Paired( SwordFighter a, SwordFighter b ) => a.InDuel && a.Opponent == b && b.Opponent == a && a.DuelId == b.DuelId;
	private static Vector3 PositionFor( bool host ) => PlatformTop + new Vector3( host ? -35 : 35, 0, 2 );
	private static bool Staged( SwordFighter host, SwordFighter client ) => host.WorldPosition.Distance( PositionFor( true ) ) < 8
		&& client.WorldPosition.Distance( PositionFor( false ) ) < 8 && host.Controller.IsOnGround && client.CanDuel;
	private static Vector3 RespawnPoint( SwordFighter fighter )
	{
		var sailor = fighter.GetComponent<ShipPlayer>();
		return sailor.IsValid() && sailor.SpawnPoint.IsValid() && sailor.Ship.IsValid() && !sailor.Ship.IsSinking
			? sailor.SpawnPoint.WorldPosition : fighter.SpawnPosition;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Issue( Stage stage, int beat = -1 )
	{
		var local = Game.ActiveScene?.GetAllComponents<SwordFighter>().FirstOrDefault( x => x.IsLocalPlayer );
		if ( stage == Stage.Restore )
		{
			if ( prepared.IsValid() )
			{
				prepared.WorldTransform = originalDeck.IsValid() ? originalDeck.WorldTransform.ToWorld( originalTransform ) : originalTransform;
				prepared.Transform.ClearInterpolation();
				prepared.Controller.EyeAngles = originalAim;
				prepared.Controller.UseLookControls = originalLook;
				prepared.Controller.UseInputControls = originalInput;
				prepared.Controller.Body.Velocity = originalDeck.IsValid() ? originalDeck.Body.GetVelocityAtPoint( prepared.WorldPosition ) + originalVelocity : originalVelocity;
			}
			if ( platform.IsValid() ) platform.Destroy();
			prepared = null;
			originalDeck = null;
			return;
		}
		if ( !local.IsValid() ) return;
		if ( stage == Stage.Prepare )
		{
			prepared = local;
			originalDeck = local.GetComponent<DeckWalkMode>()?.SupportingShip;
			originalTransform = originalDeck.IsValid() ? originalDeck.WorldTransform.ToLocal( local.WorldTransform ) : local.WorldTransform;
			originalVelocity = local.Controller.Body.Velocity - (originalDeck.IsValid() ? originalDeck.Body.GetVelocityAtPoint( local.WorldPosition ) : Vector3.Zero);
			originalAim = local.Controller.EyeAngles;
			originalInput = local.Controller.UseInputControls;
			originalLook = local.Controller.UseLookControls;
			local.Controller.UseLookControls = false;
			platform = new GameObject( local.Scene, true, "Sword duel test platform" );
			platform.NetworkMode = NetworkMode.Never;
			platform.WorldPosition = PlatformTop - Vector3.Up * 10;
			platform.AddComponent<BoxCollider>().Scale = new Vector3( 700, 700, 20 );
		}
		if ( local != prepared ) return;
		if ( stage == Stage.DrawTools ) local.Equip( Networking.IsHost ? HeldItem.Planks : HeldItem.Bucket );
		if ( stage == Stage.CheckTools && !Networking.IsHost ) _ = VerifyToolsOnClient( local );
		if ( stage == Stage.DrawSword && Networking.IsHost ) local.Equip( HeldItem.Sword );
		if ( stage is Stage.Prepare or Stage.Place )
		{
			local.WorldPosition = PositionFor( Networking.IsHost );
			local.Transform.ClearInterpolation();
			local.Controller.Body.Velocity = Vector3.Zero;
			local.Controller.EyeAngles = new Angles( 0, Networking.IsHost ? 0 : 180, 0 );
		}
		if ( stage == Stage.HostLunge && Networking.IsHost || stage == Stage.ClientLunge && !Networking.IsHost ) local.Swing();
		if ( stage == Stage.Escape && !Networking.IsHost ) local.FleeDuel();
		if ( stage == Stage.Separate && !Networking.IsHost )
		{
			local.WorldPosition = PlatformTop + new Vector3( 300, 0, 2 );
			local.Transform.ClearInterpolation();
			local.Controller.Body.Velocity = Vector3.Zero;
		}
		if ( !local.InDuel || local.BeatIndex != beat ) return;
		var expected = local.DirectionAt( beat );
		if ( stage == Stage.WrongClient && !Networking.IsHost )
		{
			local.SubmitDirection( (SwordDirection)(((int)expected + 1) % 4) );
			local.SubmitDirection( expected );
			ReportWrongPress( local.DuelId, beat );
		}
		else if ( stage == Stage.AnswerBoth || Networking.IsHost && stage is (Stage.HostAnswer or Stage.WrongClient) )
		{
			local.SubmitDirection( expected );
		}
	}

	[Rpc.Host]
	private static void ReportTools( SwordFighter owner )
	{
		if ( running && Game.IsEditor && owner.IsValid() && Rpc.Caller == owner.Network.Owner && Rpc.Caller != Connection.Local )
			clientSawTools = true;
	}

	private static async Task VerifyToolsOnClient( SwordFighter local )
	{
		var host = local.Scene.GetAllComponents<SwordFighter>().FirstOrDefault( x => !x.IsNpc && x.Network.Owner == Connection.Host );
		for ( int i = 0; i < 50 && host.IsValid() && local.IsValid(); i++ )
		{
			if ( local.EquippedItem == HeldItem.Bucket && host.EquippedItem == HeldItem.Planks )
			{
				host.Equip( HeldItem.Sword );
				if ( host.EquippedItem == HeldItem.Planks ) ReportTools( local );
				return;
			}
			await GameTask.DelaySeconds( 0.05f );
		}
	}

	[Rpc.Host]
	private static void ReportWrongPress( Guid duel, int beat )
	{
		if ( !running || !Game.IsEditor || Rpc.Caller is null || Rpc.Caller == Connection.Local ) return;
		if ( !Game.ActiveScene.GetAllComponents<SwordFighter>().Any( x => x.Network.Owner == Rpc.Caller && x.DuelId == duel ) ) return;
		reportedDuel = duel;
		reportedBeat = beat;
	}

	private static async Task WaitFor( Func<bool> ready, string message )
	{
		for ( int i = 0; i < 100 && !ready(); i++ ) await GameTask.DelaySeconds( 0.05f );
		Check( ready(), message );
	}

	private static void Check( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
		Log.Info( $"COMBAT NETWORK TEST OK: {message}" );
	}
}
