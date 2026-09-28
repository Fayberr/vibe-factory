namespace FactorySim;

/// <summary>An order: deliver <see cref="Quantity"/> of an item to any depot before the deadline.</summary>
public sealed class Contract
{
    public int Id { get; set; }
    public string Item { get; set; } = "";
    public long Quantity { get; set; }
    public long Delivered { get; set; }
    public long OfferedAtTick { get; set; }
    public long ExpiresAtTick { get; set; }

    /// <summary>Paid on completion, on top of what the items sell for.</summary>
    public BigNum Reward { get; set; }

    public bool IsComplete => Delivered >= Quantity;
}

/// <summary>The open orders and contract history of a world (saved with it).</summary>
public sealed class ContractBoard
{
    /// <summary>Orders offered at the same time.</summary>
    public const int Slots = 3;

    public List<Contract> Open { get; set; } = new();
    public int NextId { get; set; } = 1;
    public int Completed { get; set; }
    public int Expired { get; set; }
    public BigNum EarnedFromContracts { get; set; }
    public long NextOfferTick { get; set; }
}
