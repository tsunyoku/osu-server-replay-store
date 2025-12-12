// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Server.ReplayStore.Configuration;

namespace osu.Server.ReplayStore
{
    public class ExpireReplayCacheWorker : BackgroundService
    {
        private readonly ILogger<ExpireReplayCacheWorker> logger;

        public ExpireReplayCacheWorker(ILogger<ExpireReplayCacheWorker> logger)
        {
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    removeExpiredDirectories(AppSettings.ReplayCacheStoragePath);
                    removeExpiredDirectories(AppSettings.LegacyReplayCacheStoragePath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to remove expired replay directories");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
        }

        private void removeExpiredDirectories(string baseDirectory)
        {
            foreach (string cacheFolder in Directory.EnumerateDirectories(baseDirectory))
            {
                string cacheDate = Path.GetFileName(cacheFolder);

                if ((DateTime.Today - getDateFromString(cacheDate)).TotalDays <= AppSettings.ReplayCacheDays)
                    continue;

                Directory.Delete(cacheFolder, true);
                logger.LogInformation("Deleted expired cache folder {CacheFolder}", cacheFolder);
            }
        }

        private static DateTime getDateFromString(string date)
            => DateTime.ParseExact(date, "yyyyMMdd", null).Date;
    }
}
