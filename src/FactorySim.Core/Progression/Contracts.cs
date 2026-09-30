namespace FactorySim;

/// <summary>An order: deliver <see cref="Quantity"/> of an item to any depot before the deadline.</summary>
public sealed class Contract
{
    public int Id { get; set; }
    public string Item { get; set; } = "";
    public long Quantity { get; set; }
    public long Delivered { get; set; }
    /// <summary>Optional content bundle id. Empty for the original single-good orders.</summary>
    public string Bundle { get; set; } = "";
    /// <summary>Further goods requested by the same order. The original fields remain the first line for old saves.</summary>
    public List<ContractLine> Additional { get; set; } = new();
    public long OfferedAtTick { get; set; }
    public long ExpiresAtTick { get; set; }

    /// <summary>Paid on completion, on top of what the items sell for.</summary>
    public BigNum Reward { get; set; }

    public bool IsComplete => Delivered >= Quantity && Additional.All(x => x.Delivered >= x.Quantity);

    public IEnumerable<ContractLine> Lines()
    {
        yield return new ContractLine { Item = Item, Quantity = Quantity, Delivered = Delivered };
        foreach (var line in Additional) yield return line;
    }
}

public sealed class ContractLine
{
    public string Item { get; set; } = "";
    public long Quantity { get; set; }
    public long Delivered { get; set; }
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
