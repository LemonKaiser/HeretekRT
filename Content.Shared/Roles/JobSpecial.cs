namespace Content.Shared.Roles
{
    /// <summary>
    ///     Provides special hooks for when jobs get spawned in/equipped.
    /// </summary>
    [ImplicitDataDefinitionForInheritors]
    public abstract partial class JobSpecial
    {
        /// <summary>
        /// Must only be enabled by code for body-only, idempotent setup that cannot issue gear,
        /// currency, access cards, or other transferable entities.
        /// </summary>
        public virtual bool ApplyOnPersistentRestore => false;

        public abstract void AfterEquip(EntityUid mob);
    }
}
