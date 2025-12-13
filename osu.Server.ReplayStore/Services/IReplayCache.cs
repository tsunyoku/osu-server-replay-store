// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Server.ReplayStore.Services
{
    public interface IReplayCache
    {
        Task AddAsync(long scoreId, byte[] replayData);

        Task<byte[]?> FindReplayDataAsync(long scoreId);

        Task RemoveAsync(long scoreId);
    }
}
