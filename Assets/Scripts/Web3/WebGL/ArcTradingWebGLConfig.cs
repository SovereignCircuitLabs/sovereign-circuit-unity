using System;

namespace ArcTrading.WebGL
{
    /// <summary>
    /// Process-wide settings for the WebGL HTTP backend (TypeScript Express server
    /// exposing the read/write contract routes). The Desktop / Editor build does not
    /// read these values - it talks directly to the chain via Nethereum.
    ///
    /// Defaults assume the server is running on the same machine for local demos.
    /// Override at runtime with <see cref="SetApiBaseUrl"/> from a bootstrap component
    /// or from the wallet-login bridge once it knows the deployed server URL.
    /// </summary>
    public static class ArcTradingWebGLConfig
    {
        public static string ApiBaseUrl { get; private set; } = "http://localhost:4021";

        // Reserved for Phase 2-4 write routes (server enforces Bearer auth via ADMIN_TOKEN).
        public static string AdminToken { get; private set; } =
            "b2db24759d681cc760a899d0d09fcdbed0602f80df1bab37c81bbb7b081a35dc";

        public static int RequestTimeoutSeconds { get; set; } = 15;

        // Max retry attempts on transient failures (network error, HTTP 5xx).
        public static int MaxRetries { get; set; } = 2;

        // When true, ArcTradingApiClient logs each request URL + raw response body.
        // Useful for first-run shape validation against the server.
        public static bool VerboseLogging { get; set; }

        public static void SetApiBaseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            ApiBaseUrl = url.TrimEnd('/');
        }

        public static void SetAdminToken(string token)
        {
            AdminToken = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        }
    }
}