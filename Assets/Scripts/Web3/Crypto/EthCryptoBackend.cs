namespace ArcTrading.Crypto
{
    /// <summary>
    /// Static facade for the active <see cref="IEthCryptoBackend"/>. The selection
    /// is compile-time via <c>#if UNITY_WEBGL &amp;&amp; !UNITY_EDITOR</c> so that
    /// Desktop / Editor builds never link the jslib extern functions and the
    /// WebGL build never references Nethereum at the call site.
    ///
    /// To override the backend in tests, assign <see cref="Current"/> directly.
    /// </summary>
    public static class EthCryptoBackend
    {
        private static IEthCryptoBackend current;

        public static IEthCryptoBackend Current
        {
            get
            {
                if (current != null) return current;
#if UNITY_WEBGL && !UNITY_EDITOR
                current = new WebGLCryptoBackend();
#else
                current = new NethereumCryptoBackend();
#endif
                return current;
            }
            set => current = value;
        }
    }
}
