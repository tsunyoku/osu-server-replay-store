// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Globalization;
using osu.Server.ReplayStore.Configuration;

namespace osu.Server.ReplayStore.Services
{
    public class LocalReplayStorage : IReplayStorage
    {
        private readonly string baseDirectory;

        public LocalReplayStorage(string? directory = null)
        {
            baseDirectory = directory ?? AppSettings.LocalReplayStoragePath;
        }

        public async Task StoreReplayAsync(ulong scoreId, Stream replayData)
        {
            string path = getPathToReplay(scoreId);

            using var fileStream = File.OpenWrite(path);
            await replayData.CopyToAsync(fileStream);
        }

        public async Task<Stream> GetReplayStreamAsync(ulong scoreId)
        {
            string path = getPathToReplay(scoreId);

            var memoryStream = new MemoryStream();

            using var fileStream = File.OpenRead(path);
            await fileStream.CopyToAsync(memoryStream);

            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }

        public Task DeleteReplayAsync(ulong scoreId)
        {
            string path = getPathToReplay(scoreId);

            File.Delete(path);
            return Task.CompletedTask;
        }

        private string getPathToReplay(ulong scoreId) =>
            Path.Combine(baseDirectory, scoreId.ToString(CultureInfo.InvariantCulture));
    }
}
