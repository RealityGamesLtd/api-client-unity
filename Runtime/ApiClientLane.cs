namespace ApiClient.Runtime
{
    /// <summary>
    /// Selects which transport pools an <see cref="ApiClient"/> instance owns and which
    /// requests it instruments for the priority coordinator.
    /// </summary>
    public enum ApiClientLane
    {
        /// <summary>
        /// Single instance owns gameplay, stream and asset HttpClients. Default.
        /// </summary>
        Mixed = 0,

        /// <summary>
        /// Instance only services gameplay/stream traffic. Asset HttpClient is not built and
        /// <see cref="ApiClient.SendByteArrayRequest"/> falls back to the gameplay client.
        /// </summary>
        Gameplay = 1,

        /// <summary>
        /// Instance only services asset traffic. Gameplay counter is not incremented for any
        /// requests routed through this client; only the asset HttpClient is built.
        /// </summary>
        Asset = 2,
    }
}
