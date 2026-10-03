namespace NavalCombat;

public enum TransactionFailure
{
	None,
	NotAuthoritative,
	InvalidRequest,
	OutOfRange,
	NotOwner,
	InsufficientFunds,
	InsufficientGoods,
	InsufficientCapacity,
	ProductionBusy,
	ProductionNotReady,
	NoMarketListing,
	MaximumLevel,
	Destroyed
}

public readonly struct TransactionResult
{
	public bool Success { get; }
	public TransactionFailure Failure { get; }
	public int Amount { get; }
	public int TotalPrice { get; }

	private TransactionResult( bool success, TransactionFailure failure, int amount, int totalPrice )
	{
		Success = success;
		Failure = failure;
		Amount = amount;
		TotalPrice = totalPrice;
	}

	public static TransactionResult Accepted( int amount = 0, int totalPrice = 0 )
	{
		return new TransactionResult( true, TransactionFailure.None, amount, totalPrice );
	}

	public static TransactionResult Rejected( TransactionFailure failure )
	{
		return new TransactionResult( false, failure, 0, 0 );
	}
}
