namespace AionDpsMeter.Services.Services.Roster
{
    /// <summary>Raised on the packet thread by the roster processors; RosterService (UI) listens.</summary>
    public sealed class RosterEvents
    {
        public event Action<GatherInfo>? Gathered;
        public event Action<int>? MorphFinished;
        public event Action<long>? Kina;
        public event Action<EnergyInfo>? Energy;
        /// <summary>Every inventory update message, raw (pet souls are read from it by the pet tracker).</summary>
        public event Action<byte[]>? Inventory;

        internal void RaiseGathered(GatherInfo g) => Gathered?.Invoke(g);
        internal void RaiseMorph(int recipe) => MorphFinished?.Invoke(recipe);
        internal void RaiseKina(long amount) => Kina?.Invoke(amount);
        internal void RaiseEnergy(EnergyInfo e) => Energy?.Invoke(e);
        internal void RaiseInventory(byte[] msg) => Inventory?.Invoke(msg);
    }
}
