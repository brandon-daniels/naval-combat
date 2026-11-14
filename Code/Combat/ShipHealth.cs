using System;

namespace NavalCombat;

public sealed class ShipHealth : Component
{
	// Retained openings are bounded independently of the flooding defeat condition.
	public const int MaximumBreaches = 64;
	public const int HolesToSink = MaximumBreaches;
	[Property] public int MaximumHealth { get; set; } = 1000;
	[Property, Group( "Flooding" )] public float FloodCapacity { get; set; } = 1000;
	[Property, Group( "Flooding" )] public float LeakRatePerTier { get; set; } = 6;
	[Property, Group( "Flooding" )] public float FullLeakDepth { get; set; } = 30;
	[Property, Group( "Damage control" )] public float BreachMergeDistance { get; set; } = 60;
	[Property, Group( "Damage control" )] public float RepairSecondsPerTier { get; set; } = 2;
	[Sync( SyncFlags.FromHost )] public NetList<HullBreach> Breaches { get; private set; } = new();
	[Sync( SyncFlags.FromHost )] public NetList<BrokenShipPart> BrokenParts { get; private set; } = new();
	public int HoleCount => Breaches?.Count ?? 0;
	public int IntegrityPercent => IsDefeated ? 0 : Math.Clamp( Health * 100 / Math.Max( 1, MaximumHealth ), 0, 100 );
	[Sync( SyncFlags.FromHost )] public float FloodVolume { get; private set; }
	[Sync( SyncFlags.FromHost )] public float LeakRate { get; private set; }
	public float WaterVolume => FloodVolume;
	public float FloodFraction => Math.Clamp( FloodVolume / Math.Max( 1, FloodCapacity ), 0, 1 );
	public int FloodPercent => (int)MathF.Round( FloodFraction * 100 );
	public float FloodFloorLocalHeight => HoldLocalHeight( 0.94f );
	public float FloodCeilingLocalHeight => HoldLocalHeight( 3.30f );
	public float FloodSurfaceLocalHeight => FloodFloorLocalHeight + (FloodCeilingLocalHeight - FloodFloorLocalHeight) * FloodFraction;
	[Sync( SyncFlags.FromHost )] public int Health { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsDefeated { get; private set; }
	[Sync( SyncFlags.FromHost )] public int Revision { get; private set; }

	protected override void OnStart()
	{
		if ( GameplayAuthority.CanMutate && Revision == 0 ) ResetHealth();
	}

	/// <summary>Called only by the authoritative projectile after its swept collision.</summary>
	internal bool ApplyCannonHit( Vector3 position, Vector3 normal, Vector3 velocity = default )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated ) return false;
		var ship = GetComponent<ArcadeShip>();
		if ( !ship.IsValid() || !ship.WalkableModelObject.IsValid() ) return false;
		var modelPoint = ship.WalkableModelObject.WorldTransform.PointToLocal( position );
		var modelNormal = ship.WalkableModelObject.WorldRotation.Inverse * normal;
		// Keep breaches within the planking instead of growing circles across thin edges.
		float edgeClearance = Math.Min( modelPoint.z - 0.12f, 3.34f - modelPoint.z ) * ship.ShipModelScale;
		if ( MathF.Abs( modelNormal.x ) > 0.3f )
			edgeClearance = Math.Min( edgeClearance, Math.Min( modelPoint.y + 9, 8.7f - modelPoint.y ) * ship.ShipModelScale );
		else
			edgeClearance = Math.Min( edgeClearance, ((modelPoint.y > 0 ? 0.12f : 1.95f) - MathF.Abs( modelPoint.x )) * ship.ShipModelScale );
		edgeClearance = Math.Max( 16, edgeClearance );
		if ( Revision == 0 ) ResetHealth();
		normal = normal.Length > 0.1f ? normal.Normal : (position - WorldPosition).Normal;
		if ( normal.Length < 0.1f ) normal = WorldRotation.Left;
		var inherited = ship.IsValid() && ship.Body.IsValid() ? ship.Body.GetVelocityAtPoint( position ) : Vector3.Zero;
		var relative = velocity.Length > 1 ? velocity - inherited : -normal * 1200;
		var localPosition = WorldTransform.PointToLocal( position + normal * 1.5f );
		var localNormal = WorldRotation.Inverse * normal;
		int nearby = FindNearbyBreach( localPosition, localNormal );
		if ( nearby >= 0 )
		{
			var breach = Breaches[nearby];
			int previousTier = Math.Clamp( breach.Tier, 1, 3 );
			breach.Tier = Math.Min( 3, previousTier + 1 );
			float radiusLimit = breach.MaximumRadius > 0 ? breach.MaximumRadius : breach.Radius * 1.35f;
			breach.Radius = Math.Min( radiusLimit, breach.Radius * (1 + 0.3f * (breach.Tier - previousTier)) );
			breach.ImpactVelocity = WorldRotation.Inverse * relative;
			breach.CreatedAt = Time.Now;
			breach.ImpactRevision = Revision;
			Breaches[nearby] = breach;
			GetComponent<ShipStructure>()?.BlastPlanks( position, relative, breach.Sequence, breach.Radius );
			Revision++;
			return true;
		}
		if ( HoleCount >= MaximumBreaches ) return false;
		var penetration = relative.Length > 1 && Vector3.Dot( relative.Normal, normal ) < -0.05f ? relative.Normal : -normal;
		// Find the reverse face of the wall so the leak is visible inside the hold too.
		var inside = position + penetration * 12;
		var interiorNormal = -normal;
		var reverse = Scene.Trace.Ray( position + penetration * 100, position + penetration ).WithoutTags( ShipDebris.Tag ).Run();
		if ( reverse.Hit && reverse.Normal.Length > 0.5f && (reverse.GameObject == GameObject || GameObject.IsDescendant( reverse.GameObject )) )
		{
			interiorNormal = reverse.Normal.Normal;
			inside = reverse.HitPosition + interiorNormal * 1.5f;
		}
		MakeRepairAnchor( ship, modelPoint, out var repairPosition, out var repairNormal );
		Breaches.Add( new HullBreach
		{
			Position = localPosition,
			Interior = WorldTransform.PointToLocal( inside ),
			Normal = localNormal,
			InteriorNormal = WorldRotation.Inverse * interiorNormal,
			RepairPosition = repairPosition,
			RepairNormal = repairNormal,
			ImpactVelocity = WorldRotation.Inverse * relative,
			CreatedAt = Time.Now,
			Radius = Math.Min( Math.Clamp( 18 + relative.Length / 240, 18, 29 ), edgeClearance * 0.75f ),
			MaximumRadius = edgeClearance * 0.75f,
			Tier = 2,
			Sequence = Revision,
			ImpactRevision = Revision
		} );
		GetComponent<ShipStructure>()?.BlastPlanks( position, relative, Revision, Breaches.Last().Radius );
		Revision++;
		return true;
	}

	private int FindNearbyBreach( Vector3 position, Vector3 normal )
	{
		int nearest = -1;
		float distance = Math.Max( 1, BreachMergeDistance );
		for ( int i = 0; i < HoleCount; i++ )
		{
			var breach = Breaches[i];
			if ( Vector3.Dot( breach.Normal, normal ) < 0.65f ) continue;
			float candidate = (breach.Position - position).Length;
			if ( candidate > distance ) continue;
			distance = candidate;
			nearest = i;
		}
		return nearest;
	}

	/// <summary>The host samples each opening in world space, including waves and ship motion.</summary>
	protected override void OnFixedUpdate()
	{
		if ( Scene.IsEditor || !GameplayAuthority.CanMutate ) return;
		if ( IsDefeated )
		{
			LeakRate = 0;
			return;
		}
		float incoming = 0;
		foreach ( var breach in Breaches )
		{
			var mouth = GetBreachWorldPosition( breach );
			var ship = GetComponent<ArcadeShip>();
			if ( !ship.IsValid() || !ship.Ocean.IsValid() ) continue;
			float outside = ship.Ocean.HeightAt( mouth, Time.Now );
			float inside = WorldTransform.PointToWorld( new Vector3( 0, 0, FloodSurfaceLocalHeight ) ).z;
			float head = Math.Max( 0, outside - Math.Max( mouth.z, inside ) );
			float area = breach.Radius * breach.Radius / (24 * 24);
			incoming += Math.Max( 0, LeakRatePerTier ) * Math.Clamp( breach.Tier, 1, 3 )
				* area * MathF.Sqrt( head / Math.Max( 1, FullLeakDepth ) );
		}
		LeakRate = incoming;
		FloodVolume = Math.Clamp( FloodVolume + incoming * Time.Delta, 0, Math.Max( 1, FloodCapacity ) );
		if ( FloodFraction < 1 ) return;
		IsDefeated = true;
		Health = 0;
		Revision++;
	}

	public Vector3 GetBreachWorldPosition( HullBreach breach ) => WorldTransform.PointToWorld( breach.Position );
	public Vector3 GetRepairWorldPosition( HullBreach breach ) => WorldTransform.PointToWorld( breach.RepairPosition );

	/// <summary>Bow/stern voids and high planks are serviced at the nearest reachable hold wall.</summary>
	private void MakeRepairAnchor( ArcadeShip ship, Vector3 modelPoint, out Vector3 position, out Vector3 normal )
	{
		// Keep work at reachable height against the actual tapered hull.
		var anchor = new Vector3( Math.Clamp( modelPoint.x, -1.5f, 1.5f ),
			Math.Clamp( modelPoint.y, -7.0f, 4.7f ), Math.Clamp( modelPoint.z, 1.45f, 2.1f ) );
		float sideDistance = MathF.Abs( MathF.Abs( modelPoint.x ) - 1.75f );
		Vector3 inward;
		if ( modelPoint.y > 4.945f || MathF.Abs( modelPoint.y - 4.975f ) < sideDistance )
		{
			anchor.y = 4.945f;
			inward = new Vector3( 0, -1, 0 );
		}
		else if ( modelPoint.y < -7.245f || MathF.Abs( modelPoint.y + 7.275f ) < sideDistance )
		{
			anchor.y = -7.245f;
			inward = new Vector3( 0, 1, 0 );
		}
		else
		{
			float side = modelPoint.x >= 0 ? 1 : -1;
			anchor.x = side * 1.72f;
			inward = new Vector3( -side, 0, 0 );
		}
		if ( MathF.Abs( inward.x ) > 0.5f )
			anchor.x = -inward.x * (ShipHullGeometry.InnerWidth( anchor.y, anchor.z ) - 0.08f);
		position = WorldTransform.PointToLocal( ship.WalkableModelObject.WorldTransform.PointToWorld( anchor ) );
		normal = WorldRotation.Inverse * (ship.WalkableModelObject.WorldRotation * inward);
	}

	public float GetBreachSubmersion( HullBreach breach )
	{
		var ship = GetComponent<ArcadeShip>();
		if ( !ship.IsValid() || !ship.Ocean.IsValid() ) return 0;
		var mouth = GetBreachWorldPosition( breach );
		float depth = ship.Ocean.HeightAt( mouth, Time.Now ) - mouth.z;
		return Math.Clamp( depth / Math.Max( 1, FullLeakDepth ), 0, 1 );
	}

	public bool TryGetBreach( int sequence, out HullBreach breach )
	{
		foreach ( var candidate in Breaches )
		{
			if ( candidate.Sequence != sequence ) continue;
			breach = candidate;
			return true;
		}
		breach = default;
		return false;
	}

	public bool FindRepairableBreach( Vector3 worldPosition, float range, out HullBreach breach )
	{
		breach = default;
		if ( IsDefeated || range <= 0 ) return false;
		bool found = false;
		foreach ( var candidate in Breaches )
		{
			float distance = (GetRepairWorldPosition( candidate ) - worldPosition).Length;
			if ( distance > range ) continue;
			range = distance;
			breach = candidate;
			found = true;
		}
		return found;
	}

	public float RepairSecondsFor( HullBreach breach ) => Math.Max( 0.25f, RepairSecondsPerTier ) * Math.Clamp( breach.Tier, 1, 3 );

	public bool IsInsideHold( Vector3 worldPosition )
	{
		var ship = GetComponent<ArcadeShip>();
		return ship.IsValid() && ship.ContainsCargoInterior( worldPosition );
	}

	/// <summary>Bucket reach is measured from the sailor's feet inside the flooded hold.</summary>
	public bool HasFloodWaterAt( Vector3 worldPosition )
	{
		return !IsDefeated && FloodVolume > 0.01f && IsInsideHold( worldPosition )
			&& WorldTransform.PointToLocal( worldPosition ).z <= FloodSurfaceLocalHeight + 20;
	}

	private float HoldLocalHeight( float modelHeight )
	{
		var ship = GetComponent<ArcadeShip>();
		return ship.IsValid() ? ship.ShipModelVerticalOffset + modelHeight * Math.Max( 1, ship.ShipModelScale ) : -90 + modelHeight * 48.8f;
	}

	/// <summary>Caller validates the crew member, range and uninterrupted repair timer on the host.</summary>
	internal bool PatchBreach( int sequence )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated ) return false;
		for ( int i = 0; i < HoleCount; i++ )
		{
			if ( Breaches[i].Sequence != sequence ) continue;
			Breaches.RemoveAt( i );
			for ( int j = BrokenParts.Count - 1; j >= 0; j-- )
				if ( ShipStructureCatalog.Parts[BrokenParts[j].Index].IsPlank && BrokenParts[j].BreachSequence == sequence ) BrokenParts.RemoveAt( j );
			GetComponent<ShipStructure>()?.RefreshDamage();
			Revision++;
			return true;
		}
		return false;
	}

	/// <summary>Removing an opening stops its leak; accumulated water must still be bailed.</summary>
	internal float BailWater( float amount )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated || !float.IsFinite( amount ) || amount <= 0 ) return 0;
		float removed = Math.Min( FloodVolume, amount );
		FloodVolume -= removed;
		return removed;
	}

	internal bool BreakPart( int index, Vector3 velocity, int breachSequence = -1 )
	{
		if ( !GameplayAuthority.CanMutate || index < 0 || index >= ShipStructureCatalog.Parts.Length ) return false;
		if ( Revision == 0 ) ResetHealth();
		if ( BrokenParts.Any( x => x.Index == index ) ) return false;
		var broken = BrokenParts.Select( x => x.Index ).ToHashSet();
		broken.Add( index );
		bool changed;
		do
		{
			changed = false;
			for ( int i = 0; i < ShipStructureCatalog.Parts.Length; i++ )
			{
				var part = ShipStructureCatalog.Parts[i];
				if ( broken.Contains( part.Parent ) || broken.Contains( part.SecondParent ) ) changed |= broken.Add( i );
			}
		} while ( changed );
		foreach ( int part in broken.OrderBy( x => x ) )
		{
			if ( BrokenParts.Any( x => x.Index == part ) ) continue;
			BrokenParts.Add( new BrokenShipPart { Index = part, Velocity = WorldRotation.Inverse * velocity, CreatedAt = Time.Now, BreachSequence = breachSequence } );
		}
		Revision++;
		GetComponent<ShipStructure>()?.RefreshDamage();
		return true;
	}

	protected override void OnUpdate()
	{
		if ( Scene.IsEditor ) return;
		if ( !GetComponent<ShipDamageVisuals>( true ).IsValid() )
		{
			var visuals = GameObject.AddComponent<ShipDamageVisuals>();
			visuals.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		}
		if ( !GetComponent<ShipFloodVisuals>( true ).IsValid() )
		{
			var visuals = GameObject.AddComponent<ShipFloodVisuals>();
			visuals.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		}
	}

	public bool ApplyDamage( int amount )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated || amount <= 0 ) return false;
		if ( Revision == 0 ) ResetHealth();
		Health = Math.Max( 0, Health - amount );
		IsDefeated = Health == 0;
		if ( IsDefeated ) FloodVolume = Math.Max( 1, FloodCapacity );
		Revision++;
		return true;
	}

	public bool Repair( int amount )
	{
		if ( !GameplayAuthority.CanMutate || IsDefeated || amount <= 0 || Health >= MaximumHealth ) return false;
		Health = Math.Min( Math.Max( 1, MaximumHealth ), Health + amount );
		Revision++;
		return true;
	}

	public void ResetHealth()
	{
		if ( !GameplayAuthority.CanMutate ) return;
		Health = Math.Max( 1, MaximumHealth );
		Breaches.Clear();
		BrokenParts.Clear();
		FloodVolume = 0;
		LeakRate = 0;
		IsDefeated = false;
		Revision++;
	}
}

/// <summary>Ship-local impact data is retained for late joiners; effects are built locally.</summary>
public struct BrokenShipPart
{
	public int BreachSequence { get; set; }
	public int Index { get; set; }
	public Vector3 Velocity { get; set; }
	public float CreatedAt { get; set; }
}

public struct HullBreach
{
	public Vector3 Position { get; set; }
	public Vector3 Interior { get; set; }
	public Vector3 InteriorNormal { get; set; }
	public Vector3 RepairPosition { get; set; }
	public Vector3 RepairNormal { get; set; }
	public Vector3 Normal { get; set; }
	public Vector3 ImpactVelocity { get; set; }
	public float CreatedAt { get; set; }
	public float Radius { get; set; }
	public float MaximumRadius { get; set; }
	public int Tier { get; set; }
	public int Sequence { get; set; }
	public int ImpactRevision { get; set; }
}
