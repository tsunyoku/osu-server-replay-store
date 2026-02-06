// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Globalization;
using osu.Server.ReplayStore.Configuration;

namespace osu.Server.ReplayStore.Services
{
    /// <summary>
    /// Caches replays to local storage based on current date.
    ///
    /// The top-level directory of this cache is the <see cref="AppSettings.ReplayCacheStoragePath"/> folder.
    /// When a replay is added to the cache, it will be put into a folder named by the date it was added in <c>yyyyMMdd</c> format.
    /// <see cref="ExpireReplayCacheWorker"/> is a worker that will purge these folders as they get too old, depending on <see cref="AppSettings.ReplayCacheDays"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// $(AppSettings.ReplayCacheStoragePath)
    /// ├─ 20251212
    /// ├─ 20251211
    /// └─ 20251210
    /// </code>
    /// </example>
    public class FileReplayCache : IReplayCache
    {
        private readonly string baseDirectory;

        public FileReplayCache(string? directory = null)
        {
            baseDirectory = directory ?? AppSettings.ReplayCacheStoragePath;
        }

        public Task AddAsync(ulong scoreId, byte[] replayData)
        {
            return File.WriteAllBytesAsync(
                getPathToReplay(scoreId),
                replayData);
        }

        public async Task<byte[]?> FindReplayDataAsync(ulong scoreId)
        {
            foreach (string cacheDirectory in Directory.EnumerateDirectories(baseDirectory))
            {
                string replayPath = Path.Combine(cacheDirectory, scoreId.ToString(CultureInfo.InvariantCulture));

                if (File.Exists(replayPath))
                    return await File.ReadAllBytesAsync(replayPath);
            }

            return null;
        }

        public Task RemoveAsync(ulong scoreId)
        {
            foreach (string cacheDirectory in Directory.EnumerateDirectories(baseDirectory))
            {
                string replayPath = Path.Combine(cacheDirectory, scoreId.ToString(CultureInfo.InvariantCulture));

                if (File.Exists(replayPath))
                {
                    File.Delete(replayPath);
                    break;
                }
            }

            return Task.CompletedTask;
        }

        private string getReplayDirectory()
        {
            string date = DateTime.Today.ToString("yyyyMMdd");

            string datedDirectory = Path.Combine(baseDirectory, date);

            if (!Directory.Exists(datedDirectory))
                Directory.CreateDirectory(datedDirectory);

            return datedDirectory;
        }

        private string getPathToReplay(ulong scoreId) =>
            Path.Combine(getReplayDirectory(), scoreId.ToString(CultureInfo.InvariantCulture));
    }
}
