namespace ApiClient.Runtime
{
    /// <summary>
    /// Selects which transport pools an <see cref="ApiClient"/> instance owns and which
    /// requests it instruments for the priority coordinator.
    /// </summary>
    /// <remarks>
    /// In the two-instance topology, <see cref="ApiClientConnection"/> routes byte-array
    /// (asset) and stream traffic through the asset client and gameplay REST through the
    /// gameplay client.
    /// </remarks>
    public enum ApiClientLane
    {
        /// <summary>
        /// Default. Single instance owns gameplay HTTP, stream HTTP, and (when a
        /// <see cref="ApiClientOptions.PriorityCoordinator"/> is configured) a dedicated
        /// asset HTTP pool.
        /// </summary>
        Mixed = 0,

        /// <summary>
        /// Instance services gameplay-tier traffic only. The asset pool is not built, so
        /// <see cref="ApiClient.SendByteArrayRequest"/> falls back to the gameplay client
        /// when this lane is wired as the asset client of an
        /// <see cref="ApiClientConnection"/> (unusual).
        /// </summary>
        Gameplay = 1,

        /// <summary>
        /// Instance services asset and stream traffic. The gameplay pool is not built;
        /// gameplay-counter instrumentation is skipped on this instance so an
        /// <see cref="ApiClientConnection"/> wired in two-instance mode can route gameplay
        /// REST through the sibling gameplay <see cref="ApiClient"/>.
        /// </summary>
        Asset = 2,
    }
}
