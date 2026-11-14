using System;

namespace NavalCombat;

public enum SwordDirection { Up, Left, Down, Right }
public enum SwordBeatResult { Waiting, Hit, Missed }

public sealed partial class SwordFighter
{
	public const float BeatInterval = 0.95f;
	public const float HitWindow = 0.23f;
	private const float FirstBeatLead = 1.65f;
	private const float DisengageTime = 2f;
	private const float MaximumDuelSeparation = 240f;

	[Sync( SyncFlags.FromHost )] public SwordFighter Opponent { get; private set; }
	[Sync( SyncFlags.FromHost )] public Guid DuelId { get; private set; }
	[Sync( SyncFlags.FromHost )] public int BeatIndex { get; private set; }
	[Sync( SyncFlags.FromHost )] private float FirstBeatAt { get; set; }
	[Sync( SyncFlags.FromHost )] public SwordBeatResult BeatResult { get; private set; }
	[Sync( SyncFlags.FromHost )] public float DuelEndedAt { get; private set; } = -100;
	public bool InDuel => DuelId != Guid.Empty;
	public float BeatAt => FirstBeatAt + BeatIndex * BeatInterval;

	private Guid submittedDuel;
	private int submittedBeat;

	/// <summary>The host calls this after a lunge traces a living opponent.</summary>
	internal bool TryStartDuel( SwordFighter defender )
	{
		if ( !GameplayAuthority.CanMutate || !defender.IsValid() || defender == this || defender.Scene != Scene ) return false;
		if ( IsNpc || defender.IsNpc || EquippedItem != HeldItem.Sword || !CanDuel || !defender.CanDuel || InDuel || defender.InDuel ) return false;
		if ( Time.Now - Math.Max( DuelEndedAt, defender.DuelEndedAt ) < DisengageTime ) return false;
		// An idle tool is no protection from a challenge. Draw the defender's sword for the duel.
		if ( !defender.TryEquip( HeldItem.Sword ) ) return false;

		var id = Guid.NewGuid();
		var firstBeat = Time.Now + FirstBeatLead;
		EnterDuel( defender, id, firstBeat );
		defender.EnterDuel( this, id, firstBeat );
		return true;
	}

	private void EnterDuel( SwordFighter opponent, Guid id, float firstBeat )
	{
		Opponent = opponent;
		DuelId = id;
		FirstBeatAt = firstBeat;
		BeatIndex = 0;
		BeatResult = SwordBeatResult.Waiting;
		Feedback = "Get ready";
	}

	/// <summary>Mix the shared duel seed and beat number so both players can preview the same notes.</summary>
	public SwordDirection DirectionAt( int absoluteBeat )
	{
		uint value = unchecked( (uint)DuelId.GetHashCode() + (uint)absoluteBeat * 0x9e3779b9u );
		value = unchecked( (value ^ (value >> 16)) * 0x85ebca6bu );
		value = unchecked( (value ^ (value >> 13)) * 0xc2b2ae35u );
		return (SwordDirection)((value ^ (value >> 16)) & 3);
	}

	private void UpdateDuel()
	{
		if ( !InDuel ) return;
		if ( GameplayAuthority.CanMutate )
		{
			if ( EndSeparatedDuel() ) return;
			if ( !CanDuel || !Opponent.IsValid() || !Opponent.CanDuel || Opponent.DuelId != DuelId || Opponent.Opponent != this )
			{
				CancelDuel();
				return;
			}
			// Exactly one member advances the pair, including after a host change.
			if ( GameObject.Id.CompareTo( Opponent.GameObject.Id ) < 0 && Time.Now > CloseBeatAt )
			{
				ResolveBeat();
			}
		}

		if ( !InDuel || !IsLocalPlayer ) return;
		if ( Input.Pressed( "Duck" ) ) FleeDuel();
		else if ( Input.Pressed( "Forward" ) ) SubmitDirection( SwordDirection.Up );
		else if ( Input.Pressed( "Left" ) ) SubmitDirection( SwordDirection.Left );
		else if ( Input.Pressed( "Backward" ) ) SubmitDirection( SwordDirection.Down );
		else if ( Input.Pressed( "Right" ) ) SubmitDirection( SwordDirection.Right );
	}

	// Use measured network latency rather than accepting a client's claimed timestamp.
	private float InputTravelTime => Networking.IsActive && Network.Owner is { } owner && owner != Connection.Local
		? Math.Clamp( owner.Ping * 0.0005f, 0, 0.15f ) : 0;
	private float CloseBeatAt => BeatAt + HitWindow + Math.Max( InputTravelTime, Opponent.InputTravelTime );

	public void SubmitDirection( SwordDirection direction )
	{
		if ( !IsLocalPlayer || !InDuel || BeatResult != SwordBeatResult.Waiting ) return;
		if ( Time.Now < BeatAt - BeatInterval * 0.5f || (submittedDuel == DuelId && submittedBeat == BeatIndex) ) return;
		submittedDuel = DuelId;
		submittedBeat = BeatIndex;
		if ( GameplayAuthority.CanMutate ) JudgeDirection( DuelId, BeatIndex, direction );
		else RequestDirection( DuelId, BeatIndex, direction );
	}

	[Rpc.Host( NetFlags.OwnerOnly | NetFlags.Reliable | NetFlags.SendImmediate )]
	private void RequestDirection( Guid id, int beat, SwordDirection direction )
	{
		if ( Rpc.Caller == Network.Owner ) JudgeDirection( id, beat, direction );
	}

	private void JudgeDirection( Guid id, int beat, SwordDirection direction )
	{
		if ( !InDuel || DuelId != id || BeatIndex != beat || BeatResult != SwordBeatResult.Waiting ) return;
		var offset = Time.Now - InputTravelTime - BeatAt;
		if ( offset < -BeatInterval * 0.5f || offset > HitWindow ) return;
		if ( Math.Abs( offset ) <= HitWindow && direction == DirectionAt( beat ) )
		{
			BeatResult = SwordBeatResult.Hit;
			Feedback = "On beat";
		}
		else
		{
			BeatResult = SwordBeatResult.Missed;
			Feedback = offset < -HitWindow ? "Too early" : "Wrong direction";
		}
	}

	private void ResolveBeat()
	{
		var opponent = Opponent;
		if ( BeatResult == SwordBeatResult.Waiting ) BeatResult = SwordBeatResult.Missed;
		if ( opponent.BeatResult == SwordBeatResult.Waiting ) opponent.BeatResult = SwordBeatResult.Missed;
		ApplyMiss( opponent );
		opponent.ApplyMiss( this );
		if ( EndIfDefeated( opponent ) ) return;

		// Keep the rhythm steady, but give both owners a fresh window after a host stall.
		if ( Time.Now > BeatAt + BeatInterval - HitWindow )
		{
			FirstBeatAt = Time.Now - BeatIndex * BeatInterval;
			opponent.FirstBeatAt = FirstBeatAt;
		}
		BeatIndex++;
		opponent.BeatIndex = BeatIndex;
		BeatResult = SwordBeatResult.Waiting;
		opponent.BeatResult = SwordBeatResult.Waiting;
	}

	private void ApplyMiss( SwordFighter attacker )
	{
		if ( BeatResult != SwordBeatResult.Missed ) return;
		int damage = Math.Clamp( attacker.Damage, 1, 100 );
		attacker.SwingNumber++;
		TakeHit( damage );
		if ( Health > 0 ) Feedback = $"Missed · −{damage} HP";
	}

	private bool EndIfDefeated( SwordFighter opponent )
	{
		if ( Health > 0 && opponent.Health > 0 ) return false;
		bool draw = Health <= 0 && opponent.Health <= 0;
		EndDuel( draw ? "Both defeated" : Health > 0 ? "Duel won" : "Defeated · respawning" );
		opponent.EndDuel( draw ? "Both defeated" : opponent.Health > 0 ? "Duel won" : "Defeated · respawning" );
		return true;
	}

	public void FleeDuel()
	{
		if ( !IsLocalPlayer || !InDuel ) return;
		if ( GameplayAuthority.CanMutate ) Disengage( DuelId );
		else RequestDisengage( DuelId );
	}

	[Rpc.Host( NetFlags.OwnerOnly | NetFlags.Reliable | NetFlags.SendImmediate )]
	private void RequestDisengage( Guid id )
	{
		if ( Rpc.Caller == Network.Owner ) Disengage( id );
	}

	private void Disengage( Guid id )
	{
		if ( !InDuel || DuelId != id || !Opponent.IsValid() || Opponent.DuelId != id ) return;
		if ( EndSeparatedDuel() ) return;
		// A crouch just after an incorrect press cannot erase a committed miss.
		if ( Time.Now > CloseBeatAt ) ResolveBeat();
		if ( !InDuel ) return;
		var opponent = Opponent;
		ApplyMiss( opponent );
		opponent.ApplyMiss( this );
		if ( EndIfDefeated( opponent ) ) return;
		EndDuel( "You escaped" );
		opponent.EndDuel( "Opponent escaped" );
	}

	private bool EndSeparatedDuel()
	{
		if ( !GameplayAuthority.CanMutate || !InDuel || !Opponent.IsValid() || Opponent.DuelId != DuelId || Opponent.Opponent != this ) return false;
		if ( WorldPosition.Distance( Opponent.WorldPosition ) <= MaximumDuelSeparation ) return false;
		var opponent = Opponent;
		// Moving decks can pull the pair apart. Preserve committed mistakes without charging unanswered notes.
		ApplyMiss( opponent );
		opponent.ApplyMiss( this );
		if ( EndIfDefeated( opponent ) ) return true;
		EndDuel( "Opponent out of reach" );
		opponent.EndDuel( "Opponent out of reach" );
		return true;
	}

	private void CancelDuel()
	{
		if ( !GameplayAuthority.CanMutate || !InDuel ) return;
		if ( Opponent.IsValid() && Opponent.DuelId == DuelId && Opponent.Opponent == this ) Opponent.EndDuel( "Duel interrupted" );
		EndDuel( "Duel interrupted" );
	}

	private void EndDuel( string feedback )
	{
		DuelId = Guid.Empty;
		Opponent = null;
		BeatResult = SwordBeatResult.Waiting;
		DuelEndedAt = Time.Now;
		Feedback = feedback;
	}
}
