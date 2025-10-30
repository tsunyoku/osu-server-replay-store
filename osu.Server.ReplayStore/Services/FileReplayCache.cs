// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Globalization;
using osu.Server.ReplayStore.Configuration;
using osu.Server.ReplayStore.Helpers;

namespace osu.Server.ReplayStore.Services
{
    /// <summary>
    /// Caches replays to local storage based on score type (legacy, solo) and current date.
    ///
    /// The top level of this cache are the <see cref="AppSettings.ReplayCacheStoragePath"/> and <see cref="AppSettings.LegacyReplayCacheStoragePath"/> folders.
    /// In the case of the legacy cache folder, replays must be split by ruleset so there is another hierarchy level inside with a folder per ruleset
    /// The cache is divided up by folders of each date. When a replay is added to the cache, it will be put into a folder with the date it was added.
    /// <see cref="ExpireReplayCacheWorker"/> is a worker that will purge these folders as they get too old, depending on <see cref="AppSettings.ReplayCacheDays"/>.
    ///
    /// Examples:
    ///
    /// - A solo score with score ID 1 that was cached on 30/10/25 would be stored in {ReplayCacheStoragePath}/301025/1.
    /// - A legacy score with score ID 1 for the osu ruleset that was cached on 30/10/25 would be stored in {LegacyReplayCacheStoragePath}/osu/301025/1.
    /// </summary>
    public class FileReplayCache : IReplayCache
    {
        private readonly string baseDirectory;
        private readonly string legacyBaseDirectory;

        public FileReplayCache(string? directory = null, string? legacyDirectory = null)
        {
            baseDirectory = directory ?? AppSettings.ReplayCacheStoragePath;
            legacyBaseDirectory = legacyDirectory ?? AppSettings.LegacyReplayCacheStoragePath;
        }

        public Task AddAsync(long scoreId, ushort rulesetId, bool legacyScore, byte[] replayData)
        {
            return File.WriteAllBytesAsync(
                getPathToReplay(scoreId, rulesetId, legacyScore),
                replayData);
        }

        public async Task<byte[]?> FindReplayDataAsync(long scoreId, ushort rulesetId, bool legacyScore)
        {
            string baseCacheDirectory = legacyScore
                ? Path.Join(legacyBaseDirectory, LegacyRulesetHelper.GetRulesetNameFromLegacyId(rulesetId))
                : baseDirectory;

            foreach (string cacheDirectory in Directory.EnumerateDirectories(baseCacheDirectory))
            {
                string replayPath = Path.Combine(cacheDirectory, scoreId.ToString(CultureInfo.InvariantCulture));

                if (File.Exists(replayPath))
                    return await File.ReadAllBytesAsync(replayPath);
            }

            return null;
        }

        public Task RemoveAsync(long scoreId, ushort rulesetId, bool legacyScore)
        {
            string baseCacheDirectory = legacyScore
                ? Path.Join(legacyBaseDirectory, LegacyRulesetHelper.GetRulesetNameFromLegacyId(rulesetId))
                : baseDirectory;

            foreach (string cacheDirectory in Directory.EnumerateDirectories(baseCacheDirectory))
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

        private string getReplayDirectory(ushort rulesetId, bool legacyScore)
        {
            string date = DateTime.Today.ToString("ddMMyy");

            string baseCacheDirectory = legacyScore
                ? Path.Join(legacyBaseDirectory, LegacyRulesetHelper.GetRulesetNameFromLegacyId(rulesetId))
                : baseDirectory;

            string datedDirectory = Path.Combine(baseCacheDirectory, date);

            if (!Directory.Exists(datedDirectory))
                Directory.CreateDirectory(datedDirectory);

            return datedDirectory;
        }

        private string getPathToReplay(long scoreId, ushort rulesetId, bool legacyScore) =>
            Path.Combine(getReplayDirectory(rulesetId, legacyScore), scoreId.ToString(CultureInfo.InvariantCulture));
    }
}
