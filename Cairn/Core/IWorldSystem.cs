namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// Every system implements this and registers with <see cref="CairnTick"/>.
    ///
    /// Systems do not own timers, coroutines, or Update methods of their own. CairnTick
    /// decides when they run and how much time they may spend, which is what makes global
    /// throttling possible from one place instead of one per system.
    /// </summary>
    public interface IWorldSystem
    {
        /// <summary>Log-facing name. Also the config section name.</summary>
        string Name { get; }

        /// <summary>
        /// Config-backed master switch. Read every tick, never cached, so an admin toggling
        /// it at runtime takes effect without a restart.
        /// </summary>
        bool Enabled { get; }

        /// <summary>Desired seconds between ticks. CairnTick may run it later under load, never sooner.</summary>
        float IntervalSeconds { get; }

        /// <summary>
        /// Called once, after config binding and after ZNet exists. Do resolution work here,
        /// not in a constructor — reflection into game internals cannot succeed before the
        /// game has loaded.
        /// </summary>
        void Initialise();

        /// <summary>
        /// One pass. <paramref name="deltaSeconds"/> is the real time since this system's own
        /// previous tick — not frame delta, and not the value its neighbours receive.
        ///
        /// Must return promptly. Anything expensive belongs behind a cursor that resumes on
        /// the next tick rather than a loop that finishes inside one.
        /// </summary>
        void Tick(float deltaSeconds);
    }
}
