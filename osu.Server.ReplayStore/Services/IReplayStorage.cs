// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.ReplayStore.Services
{
    public interface IReplayStorage
    {
        Task StoreReplayAsync(ulong scoreId, Stream replayData);

        Task<Stream> GetReplayStreamAsync(ulong scoreId);

        Task DeleteReplayAsync(ulong scoreId);
    }
}
