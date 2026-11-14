using System;

namespace NavalCombat;

/// <summary>Owner input requests; repair clocks and bucket transfers are validated by the host.</summary>
public sealed partial class ShipPlayer
{
	[Property, Group( "Damage control" )] public float RepairReach { get; set; } = 90;
	[Property, Group( "Damage control" )] public float BucketCapacity { get; set; } = 100;
	[Property, Group( "Damage control" )] public float BucketSeconds { get; set; } = 0.7f;
	[Sync( SyncFlags.FromHost )] public int RepairSequence { get; private set; } = -1;
	[Sync( SyncFlags.FromHost )] public float RepairStartedAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public float RepairFinishesAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public float BucketWater { get; private set; }
	[Sync( SyncFlags.FromHost )] public float BucketReadyAt { get; private set; }
	public bool IsRepairing => RepairSequence >= 0;
	public bool IsUsingBucket => BucketReadyAt > 0;
	public bool BucketFull => BucketWater > 0;
	public float RepairProgress => IsRepairing ? Math.Clamp( (Time.Now - RepairStartedAt) / Math.Max( 0.1f, RepairFinishesAt - RepairStartedAt ), 0, 1 ) : 0;
	private ShipHealth DamageHull => Ship.IsValid() ? Ship.GetComponent<ShipHealth>() : null;
	private float repairLeaseUntil;
	private int repairImpactRevision;
	private float nextRepairRequest;
	private int lastRepairRequest = -1;
	private bool bucketThrow;

	private bool HandsAvailable => Enabled && !InSwordCombat && Controller.IsValid() && Controller.Enabled && Ship.IsValid() && !Ship.IsSinking
		&& !CurrentStation.IsValid() && !CarriedBarrel.IsValid() && GetComponent<PlayerVoyage>() is not { InvestmentOpen: true };
	private bool Holding( HeldItem item ) => GetComponent<SwordFighter>() is { CanHoldItem: true } fighter && fighter.EquippedItem == item;

	public int NearbyBreachSequence
	{
		get
		{
			if ( !DamageHull.IsValid() ) return -1;
			int closest = -1;
			float distance = float.MaxValue;
			foreach ( var candidate in DamageHull.Breaches )
			{
				if ( !BreachInReach( candidate ) ) continue;
				float next = WorldPosition.Distance( DamageHull.GetRepairWorldPosition( candidate ) );
				if ( next >= distance ) continue;
				distance = next;
				closest = candidate.Sequence;
			}
			return closest;
		}
	}
	public bool WaterInReach => HandsAvailable && DamageHull.IsValid() && DamageHull.HasFloodWaterAt( WorldPosition );
	public bool CanScoop => Holding( HeldItem.Bucket ) && WaterInReach;
	public bool CanThrowOverboard
	{
		get
		{
			if ( !Holding( HeldItem.Bucket ) || !HandsAvailable || !Ship.WalkableModelObject.IsValid() ) return false;
			var transform = Ship.WalkableModelObject.WorldTransform;
			var feet = transform.PointToLocal( WorldPosition );
			if ( MathF.Abs( feet.x ) > 3.5f || feet.y < -9.2f || feet.y > 9 || feet.z < 3.1f || feet.z > 6 ) return false;
			var end = Controller.EyePosition + Controller.EyeTransform.Rotation.Forward * 180;
			var aim = transform.PointToLocal( end );
			if ( MathF.Abs( aim.x ) < 3.35f && aim.y > -9.3f && aim.y < 9.0f ) return false;
			var trace = Scene.Trace.Ray( Controller.EyePosition, end ).WithoutTags( ShipDebris.Tag )
				.IgnoreGameObjectHierarchy( GameObject ).Run();
			return !trace.Hit;
		}
	}

	private void ReadDamageControlInput()
	{
		int sequence = Holding( HeldItem.Planks ) && Input.Down( "Attack1" ) ? NearbyBreachSequence : -1;
		SetRepairing( sequence, sequence >= 0 );
		if ( Holding( HeldItem.Bucket ) && HandsAvailable && Input.Pressed( "Attack2" ) ) UseBucket();
	}

	public void SetRepairing( int sequence, bool held )
	{
		int requested = held ? sequence : -1;
		if ( Networking.IsActive )
		{
			if ( IsProxy ) return;
			if ( requested == lastRepairRequest && (requested < 0 || Time.Now < nextRepairRequest) ) return;
			lastRepairRequest = requested;
			nextRepairRequest = Time.Now + 0.12f;
			RequestRepair( requested );
		}
		else AcceptRepair( requested );
	}

	[Rpc.Host]
	private void RequestRepair( int sequence )
	{
		if ( !Ship.IsValid() || Network.Owner != Rpc.Caller || Ship.Network.Owner != Rpc.Caller ) return;
		AcceptRepair( sequence );
	}

	private bool CanRepair( int sequence, out HullBreach breach )
	{
		breach = default;
		return Holding( HeldItem.Planks ) && DamageHull.IsValid() && DamageHull.TryGetBreach( sequence, out breach ) && BreachInReach( breach );
	}

	private bool BreachInReach( HullBreach breach )
	{
		if ( !HandsAvailable || !DamageHull.IsInsideHold( WorldPosition ) ) return false;
		var repairPoint = DamageHull.GetRepairWorldPosition( breach );
		if ( WorldPosition.Distance( repairPoint ) > Math.Clamp( RepairReach, 20, 110 ) ) return false;
		var trace = Scene.Trace.Ray( WorldPosition + Ship.WorldRotation.Up * 40, repairPoint )
			.WithoutTags( ShipDebris.Tag ).IgnoreGameObjectHierarchy( GameObject ).Run();
		return !trace.Hit || trace.HitPosition.Distance( repairPoint ) < 8;
	}

	private void AcceptRepair( int sequence )
	{
		if ( !GameplayAuthority.CanMutate ) return;
		if ( sequence < 0 || !CanRepair( sequence, out var breach ) ) { CancelRepair(); return; }
		repairLeaseUntil = Time.Now + 0.6f;
		if ( RepairSequence == sequence ) return;
		RepairSequence = sequence;
		repairImpactRevision = breach.ImpactRevision;
		RepairStartedAt = Time.Now;
		RepairFinishesAt = Time.Now + DamageHull.RepairSecondsFor( breach );
	}

	private void CancelRepair()
	{
		if ( !IsRepairing ) return;
		RepairSequence = -1;
		RepairStartedAt = 0;
		RepairFinishesAt = 0;
	}

	internal void CancelDamageControl()
	{
		if ( !GameplayAuthority.CanMutate ) return;
		CancelRepair();
		BucketReadyAt = 0;
	}

	public void UseBucket()
	{
		if ( Networking.IsActive ) { if ( !IsProxy ) RequestBucket(); }
		else AcceptBucket();
	}

	[Rpc.Host]
	private void RequestBucket()
	{
		if ( !Ship.IsValid() || Network.Owner != Rpc.Caller || Ship.Network.Owner != Rpc.Caller ) return;
		AcceptBucket();
	}

	private void AcceptBucket()
	{
		if ( !GameplayAuthority.CanMutate || IsUsingBucket || !(BucketFull ? CanThrowOverboard : CanScoop) ) return;
		CancelRepair();
		bucketThrow = BucketFull;
		BucketReadyAt = Time.Now + Math.Clamp( BucketSeconds, 0.3f, 3 );
	}

	private void UpdateDamageControl()
	{
		if ( !GameplayAuthority.CanMutate || Scene.IsEditor ) return;
		if ( IsRepairing )
		{
			if ( Time.Now > repairLeaseUntil || !CanRepair( RepairSequence, out var breach ) || breach.ImpactRevision != repairImpactRevision ) CancelRepair();
			else if ( Time.Now >= RepairFinishesAt )
			{
				DamageHull.PatchBreach( RepairSequence );
				CancelRepair();
			}
		}
		if ( !IsUsingBucket ) return;
		if ( !(bucketThrow ? CanThrowOverboard : CanScoop) ) { BucketReadyAt = 0; return; }
		if ( Time.Now < BucketReadyAt ) return;
		BucketReadyAt = 0;
		if ( bucketThrow ) BucketWater = 0;
		else BucketWater = DamageHull.BailWater( Math.Clamp( BucketCapacity, 1, 200 ) );
	}
}
