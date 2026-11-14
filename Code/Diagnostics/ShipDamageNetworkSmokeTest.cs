using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NavalCombat;

/// <summary>Two-peer damage replication and owner-simulated sinking, on a disposable remote-owned hull.</summary>
public static class ShipDamageNetworkSmokeTest
{
	private static string pending;
	private static readonly Dictionary<Guid, string> reports = new();

	[ConCmd( "naval_test_ship_damage_network" )]
	public static async Task Run()
	{
		if ( !Game.IsEditor || !Networking.IsHost || !Networking.IsActive || pending is not null ) return;
		var remote = Connection.All.FirstOrDefault( x => x != Connection.Local && x.IsActive );
		var sailor = Game.ActiveScene?.GetAllComponents<ShipPlayer>().FirstOrDefault( x => !x.IsNpc && x.Network.Owner == Connection.Local );
		if ( remote is null || !sailor.IsValid() ) { Log.Warning( "SHIP DAMAGE NETWORK TEST needs a host and client." ); return; }
		GameObject fixture = null;
		pending = Guid.NewGuid().ToString();
		try
		{
			// A preview damaged before hosting also exercises late-join reconstruction.
			var existing = sailor.Ship.GetComponent<ShipHealth>();
			if ( existing.HoleCount > 0 ) await CheckPeers( existing, existing.HoleCount, true );
			if ( existing.BrokenParts.Count > 0 ) await CheckStructure( existing, true );
			fixture = sailor.Ship.GameObject.Clone( new Vector3( 0, -6000, 500 ), Rotation.Identity );
			fixture.Name = "Network damage test fixture";
			fixture.Enabled = true;
			fixture.NetworkMode = NetworkMode.Object;
			var target = fixture.GetComponent<ArcadeShip>();
			var originalOcean = target.Ocean;
			var health = fixture.GetComponent<ShipHealth>();
			health.ResetHealth();
			target.Body.MotionEnabled = false;
			fixture.NetworkSpawn( remote );
			await GameTask.DelaySeconds( 1 );
			SetMotion( target, false );
			await GameTask.DelaySeconds( 0.5f );
			var structure = target.GetComponent<ShipStructure>();
			await ShipStructureSmokeTest.FireAt( sailor.Ship, target, structure.Parts.First( x => x.PartName.StartsWith( "Gunwale_2_-1" ) ).WorldPosition );
			if ( health.BrokenParts.Count != 1 || health.HoleCount != 0 ) throw new InvalidOperationException( "Remote-owned railing did not break independently" );
			await CheckStructure( health );
			await ShipStructureSmokeTest.FireAt( sailor.Ship, target, structure.Parts.First( x => x.PartName == "Mast main" ).WorldPosition );
			if ( health.BrokenParts.Count < 3 || target.Sails.RigSupported || health.HoleCount != 0 ) throw new InvalidOperationException( "Remote-owned mast supports did not fail" );
			await CheckStructure( health );
			for ( int i = 0; i < ShipHealth.MaximumBreaches; i++ )
			{
				await ShipDamageSmokeTest.FireAtHull( sailor.Ship, target, i, true );
				if ( health.HoleCount != i + 1 ) throw new InvalidOperationException( $"Host impact {i + 1} did not damage remote-owned ship" );
				if ( health.IsDefeated || health.FloodVolume > 0 ) throw new InvalidOperationException( "Dry impacts incorrectly flooded or sank the ship" );
				if ( i == 8 || i == 9 ) await CheckPeers( health, i + 1 );
			}
			var first = health.Breaches[0];
			await ShipDamageSmokeTest.FireAtHull( sailor.Ship, target, 0, true );
			if ( health.HoleCount != 10 || !health.TryGetBreach( first.Sequence, out var widened ) || widened.Tier != 3
				|| widened.ImpactRevision <= first.ImpactRevision ) throw new InvalidOperationException( "Repeat impact did not widen the same breach" );
			await CheckPeers( health, 10, false, 11 );
			SetTide( target, target.WorldPosition.z + 100 );
			await GameTask.DelaySeconds( 1 );
			if ( health.FloodVolume <= 0 || health.LeakRate <= 120 ) throw new InvalidOperationException( "Host did not accumulate tiered flood water on remote-owned ship" );
			SetTide( target, target.WorldPosition.z - 100 );
			await GameTask.DelaySeconds( 0.3f );
			float water = health.FloodVolume;
			if ( !health.PatchBreach( first.Sequence ) || health.FloodVolume != water || health.HoleCount != 9 )
				throw new InvalidOperationException( "Patching did not remove exactly one breach while retaining water" );
			if ( health.BailWater( 25 ) != 25 || MathF.Abs( health.FloodVolume - (water - 25) ) > 0.01f )
				throw new InvalidOperationException( "Host bailing did not remove the requested volume" );
			await CheckPeers( health, 9, false, 11 );
			SetTide( target, target.WorldPosition.z + 100 );
			for ( int tick = 0; tick < 140 && !health.IsDefeated; tick++ ) await GameTask.DelaySeconds( 0.1f );
			if ( !health.IsDefeated || health.FloodFraction != 1 || health.HoleCount != 9 )
				throw new InvalidOperationException( "Full water capacity did not defeat a nine-hole ship" );
			await CheckPeers( health, 9, false, 11 );
			RestoreOcean( target, originalOcean );
			float height = target.WorldPosition.z;
			SetMotion( target, true );
			await GameTask.DelaySeconds( 4 );
			if ( target.WorldPosition.z >= height - 150 ) throw new InvalidOperationException( "Remote owner did not sink the defeated ship" );
			Log.Info( "SHIP DAMAGE NETWORK TEST PASSED: remote-owned rail/mast destruction, tiered breaches, flood volume, patching and bailing agree on both peers; client mutations rejected; full-hold owner sinking replicates." );
		}
		catch ( Exception e ) { Log.Error( $"SHIP DAMAGE NETWORK TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( fixture.IsValid() ) fixture.Destroy();
			pending = null;
			reports.Clear();
		}
	}

	[ConCmd( "naval_test_penetration_network" )]
	public static async Task RunPenetration()
	{
		if ( !Game.IsEditor || !Networking.IsHost || pending is not null ) return;
		var remote = Connection.All.FirstOrDefault( x => x != Connection.Local && x.IsActive );
		var sailor = Game.ActiveScene.GetAllComponents<ShipPlayer>().First( x => !x.IsNpc && x.Network.Owner == Connection.Local );
		if ( remote is null ) { Log.Error( "PENETRATION NETWORK TEST needs another client" ); return; }
		GameObject fixture = null;
		pending = Guid.NewGuid().ToString();
		try
		{
			fixture = sailor.Ship.GameObject.Clone( new Vector3( 0, -18000, 500 ), Rotation.Identity );
			fixture.Name = "Penetration network fixture";
			fixture.Enabled = true;
			fixture.NetworkMode = NetworkMode.Object;
			var ship = fixture.GetComponent<ArcadeShip>();
			var health = fixture.GetComponent<ShipHealth>();
			health.ResetHealth();
			health.FloodCapacity = 100;
			health.LeakRatePerTier = 3;
			ship.Body.MotionEnabled = false;
			fixture.NetworkSpawn( remote );
			await GameTask.DelaySeconds( 1 );
			SetMotion( ship, false );
			SetTide( ship, 0 );
			await GameTask.DelaySeconds( 0.5f );
			var model = ship.WalkableModelObject;
			var outward = model.WorldRotation * Vector3.Forward;
			var mouth = model.WorldTransform.PointToWorld( new Vector3( 3.1f, -1, 1.7f ) );
			var ball = new GameObject( ship.Scene, true, "Network solid shot" );
			ball.NetworkMode = NetworkMode.Never;
			ball.WorldPosition = mouth + outward * 100;
			var shot = ball.AddComponent<Cannonball>();
			shot.Source = sailor.Ship;
			shot.Velocity = -outward * 1800;
			await GameTask.DelaySeconds( 0.6f );
			if ( health.HoleCount < 2 ) throw new InvalidOperationException( "Shot did not penetrate both sides" );
			await CheckPeers( health, health.HoleCount );
			await CheckStructure( health );
			health.FloodCapacity = 100;
			health.LeakRatePerTier = 3;
			SetTide( ship, 660 );
			await GameTask.DelaySeconds( 0.5f );
			SetTide( ship, 0 );
			await GameTask.DelaySeconds( 0.5f );
			if ( health.FloodVolume <= 0 ) throw new InvalidOperationException( "No flood water" );
			await CheckPeers( health, health.HoleCount );
			SetTide( ship, 800 );
			for ( int i = 0; i < 100 && !health.IsDefeated; i++ ) await GameTask.DelaySeconds( 0.1f );
			if ( !health.IsDefeated ) throw new InvalidOperationException( "Flood did not sink ship" );
			await CheckPeers( health, health.HoleCount );
			float height = ship.WorldPosition.z;
			SetMotion( ship, true );
			await GameTask.DelaySeconds( 3 );
			if ( ship.WorldPosition.z > height - 50 ) throw new InvalidOperationException( "Remote-owned wreck did not descend" );
			Log.Info( "PENETRATION NETWORK TEST PASSED: shared entry/exit damage, missing collision, rising water, authority and remote-owned sinking" );
		}
		catch ( Exception e ) { Log.Error( $"PENETRATION NETWORK TEST FAILED: {e.Message}" ); }
		finally
		{
			if ( fixture.IsValid() ) fixture.Destroy();
			pending = null;
			reports.Clear();
		}
	}

	public static async Task CheckRetainedDamage( ShipHealth health )
	{
		if ( !Networking.IsHost || pending is not null || !health.IsValid() ) return;
		pending = Guid.NewGuid().ToString();
		try
		{
			await CheckPeers( health, health.HoleCount, true );
			await CheckStructure( health, true );
			Log.Info( "RETAINED COMBAT NETWORK TEST PASSED" );
		}
		catch ( Exception e ) { Log.Error( $"RETAINED COMBAT NETWORK TEST FAILED: {e.Message}" ); }
		finally { pending = null; reports.Clear(); }
	}

	private static async Task CheckPeers( ShipHealth health, int count, bool retained = false, int expectedBursts = -1 )
	{
		reports.Clear();
		var peers = Connection.All.Select( x => x.Id ).ToArray();
		await GameTask.DelaySeconds( 1 );
		if ( health.HoleCount != count ) throw new InvalidOperationException( "Host breach count changed before peer probe" );
		Probe( pending, health, health.Breaches.ToArray(), health.FloodVolume, health.LeakRate, health.IsDefeated,
			expectedBursts < 0 ? count : expectedBursts, retained );
		for ( int i = 0; i < 40 && !peers.All( reports.ContainsKey ); i++ ) await GameTask.DelaySeconds( 0.1f );
		if ( !peers.All( reports.ContainsKey ) ) throw new InvalidOperationException( "Missing peer damage acknowledgement" );
		foreach ( var peer in peers )
			if ( reports[peer] != "OK" ) throw new InvalidOperationException( reports[peer] );
		Log.Info( $"SHIP DAMAGE NETWORK TEST OK: all peers verified {count} breaches, {health.FloodPercent}% water, defeat={health.IsDefeated} and authority" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void SetTide( ArcadeShip ship, float height )
	{
		if ( !ship.IsValid() ) return;
		var tide = ship.GameObject.GetComponentsInChildren<ArcadeOcean>( true, true ).FirstOrDefault();
		if ( !tide.IsValid() )
		{
			var tideObject = new GameObject( ship.GameObject, false, "Network damage test tide" );
			tideObject.NetworkMode = NetworkMode.Never;
			tide = tideObject.AddComponent<ArcadeOcean>();
		}
		tide.WaveHeight = 0;
		tide.SeaLevel = height;
		ship.Ocean = tide;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void RestoreOcean( ArcadeShip ship, ArcadeOcean ocean )
	{
		if ( ship.IsValid() ) ship.Ocean = ocean;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void SetMotion( ArcadeShip ship, bool enabled )
	{
		if ( !ship.IsValid() || ship.IsProxy ) return;
		ship.Body.MotionEnabled = enabled;
		ship.Body.Velocity = Vector3.Zero;
	}

	private static async Task CheckStructure( ShipHealth health, bool retained = false )
	{
		reports.Clear();
		var peers = Connection.All.Select( x => x.Id ).ToArray();
		await GameTask.DelaySeconds( 1 );
		ProbeStructure( pending, health, health.BrokenParts.Select( x => x.Index ).ToArray(), health.GetComponent<ArcadeShip>().Sails.RigSupported, retained );
		for ( int i = 0; i < 40 && !peers.All( reports.ContainsKey ); i++ ) await GameTask.DelaySeconds( 0.1f );
		if ( !peers.All( reports.ContainsKey ) ) throw new InvalidOperationException( "Missing peer structure acknowledgement" );
		foreach ( var peer in peers )
			if ( reports[peer] != "OK" ) throw new InvalidOperationException( reports[peer] );
		Log.Info( $"SHIP STRUCTURE NETWORK TEST OK: all peers verified {health.BrokenParts.Count} removed parts, support and authority" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void ProbeStructure( string token, ShipHealth health, int[] broken, bool sailSupported, bool retained )
	{
		string error = "OK";
		var structure = health.IsValid() ? health.GetComponent<ShipStructure>() : null;
		if ( !structure.IsValid() ) error = "Ship structure did not reconstruct";
		else if ( !health.BrokenParts.Select( x => x.Index ).SequenceEqual( broken ) ) error = "Broken part identities differ";
		else if ( structure.VisiblePartCount != ShipStructureCatalog.Parts.Length - broken.Length ) error = "Missing sections did not match shared state";
		else if ( structure.Parts.Any( x => x.GameObject.Enabled == broken.Contains( x.Index ) ) ) error = "Part visibility or collision differs";
		else if ( structure.SailSupported != sailSupported ) error = "Sail support differs";
		else if ( retained && !Networking.IsHost && structure.DetachedPartCount != 0 ) error = "Late join replayed old detached beams";
		else if ( !Networking.IsHost && health.BreakPart( 0, Vector3.Left ) ) error = "Client was able to break a part";
		Log.Info( $"SHIP STRUCTURE NETWORK VIEW: host={Networking.IsHost}; parts={health?.BrokenParts.Count}; result={error}" );
		Report( token, error );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Probe( string token, ShipHealth health, HullBreach[] impacts, float water, float leakRate, bool defeated, int expectedBursts, bool retained )
	{
		string error = "OK";
		int count = impacts.Length;
		if ( !health.IsValid() ) error = "Ship health did not replicate";
		else if ( health.HoleCount != count || health.IsDefeated != defeated ) error = "Hole count or flood defeat state differs";
		else if ( !health.Breaches.SequenceEqual( impacts ) ) error = "Breach identity, tier, impact revision or geometry differs";
		else if ( MathF.Abs( health.FloodVolume - water ) > Math.Max( 0.1f, leakRate ) ) error = "Flood volume did not replicate";
		else if ( MathF.Abs( health.LeakRate - leakRate ) > 0.1f ) error = "Leak rate did not replicate";
		else if ( health.GetComponent<ShipDamageVisuals>()?.VisibleHoleCount != count ) error = "Breach effects did not reconstruct";
		else if ( !retained && health.GetComponent<ShipDamageVisuals>().ImpactBursts != expectedBursts ) error = "Live impacts did not emit exactly one burst per hit on this peer";
		else if ( retained && !Networking.IsHost && health.GetComponent<ShipDamageVisuals>().ImpactBursts != 0 ) error = "Late join replayed historical impact bursts";
		else if ( health.GetComponent<ShipDamageVisuals>().ActiveDebrisCount > health.GetComponent<ShipDamageVisuals>().DebrisLimit ) error = "Peer exceeded debris budget";
		else if ( !Networking.IsHost && health.ApplyCannonHit( health.WorldPosition, Vector3.Left ) ) error = "Client was able to mutate damage";
		else if ( !Networking.IsHost && count > 0 && health.PatchBreach( impacts[0].Sequence ) ) error = "Client was able to patch without host validation";
		else if ( !Networking.IsHost && health.BailWater( 100 ) != 0 ) error = "Client was able to bail without host validation";
		Log.Info( $"SHIP DAMAGE NETWORK VIEW: host={Networking.IsHost}; holes={health?.HoleCount}; water={health?.FloodPercent}%; result={error}" );
		Report( token, error );
	}

	[Rpc.Host]
	private static void Report( string token, string error )
	{
		if ( !Game.IsEditor || pending != token || Rpc.Caller is null ) return;
		if ( Connection.All.Any( x => x.Id == Rpc.Caller.Id ) ) reports[Rpc.Caller.Id] = error;
	}
}
