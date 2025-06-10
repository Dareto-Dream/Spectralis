using System;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Streaming
{
    public static class SpotifyTokenFixer
    {
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private static string _lastGoodToken;
        private static DateTime _lastRefresh = DateTime.MinValue;

        public static async Task<string> GetSafeTokenAsync(SpotifyTokenManager manager, CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                if (_lastGoodToken != null && (DateTime.UtcNow - _lastRefresh).TotalSeconds < 50)
                    return _lastGoodToken;

                _lastGoodToken = await manager.GetValidAccessTokenAsync(ct);
                _lastRefresh = DateTime.UtcNow;
                return _lastGoodToken;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
