namespace Content.Shared.Lathe;

public sealed partial class LatheComponent
{
    public const int MaxQueuedBatches = 50;

    [ViewVariables]
    public int? PrintingBatch;
}

public sealed partial class LatheRecipeBatch
{
    public const int MaxItemsRequested = 9999;
}
