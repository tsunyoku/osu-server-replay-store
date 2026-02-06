// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using osu.Framework.Extensions;
using osu.Server.QueueProcessor;
using osu.Server.ReplayStore.Helpers;
using osu.Server.ReplayStore.Models.Database;
using osu.Server.ReplayStore.Services;
using StatsdClient;

namespace osu.Server.ReplayStore
{
    [Route("replays")]
    public class ReplayStoreController : Controller
    {
        private const string content_type = "application/x-osu-replay";

        private readonly IReplayStorage replayStorage;
        private readonly IReplayCache replayCache;

        public ReplayStoreController(IReplayStorage replayStorage, IReplayCache replayCache)
        {
            this.replayStorage = replayStorage;
            this.replayCache = replayCache;
        }

        /// <summary>
        /// Uploads a new replay.
        /// </summary>
        /// <response code="204">The replay was uploaded successfully.</response>
        /// <response code="404">The given score ID could not be found in the database.</response>
        [HttpPut]
        [Route("{scoreId:long}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PutReplayAsync(
            [FromRoute] ulong scoreId,
            IFormFile replayFile)
        {
            Score? score;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                score = await db.GetScoreAsync(scoreId);
            }

            if (score == null)
                return NotFound();

            byte[] replayBytes;

            using (var replayStream = replayFile.OpenReadStream())
                replayBytes = await replayStream.ReadAllRemainingBytesToArrayAsync();

            if (score.IsLegacyScore)
            {
                using var db = await DatabaseAccess.GetConnectionAsync();

                using var replayStream = await createLegacyReplayWithHeadersAsync(
                    replayBytes,
                    score.ruleset_id,
                    score,
                    db);

                replayBytes = await replayStream.ReadAllBytesToArrayAsync();
            }

            using (var replayStream = new MemoryStream(replayBytes))
                await replayStorage.StoreReplayAsync(scoreId, replayStream);

            await replayCache.AddAsync(score.id, replayBytes);

            DogStatsd.Increment("replays_uploaded");
            return NoContent();
        }

        /// <summary>
        /// Fetches the replay for a score.
        /// </summary>
        /// <response code="200">The replay was downloaded successfully.</response>
        /// <response code="404">The given score ID could not be found in the database, or the score has no replay.</response>
        [HttpGet]
        [Route("{scoreId:long}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [Produces(content_type)]
        public async Task<IActionResult> GetReplayAsync([FromRoute] ulong scoreId)
        {
            Score? score;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                score = await db.GetScoreAsync(scoreId);
            }

            if (score == null || !score.has_replay)
                return NotFound();

            string fileName = createFileName(scoreId, score.beatmap_id, score.ruleset_id);

            byte[]? cachedReplay = await replayCache.FindReplayDataAsync(scoreId);

            if (cachedReplay != null)
            {
                DogStatsd.Increment("replays_downloaded", tags: ["source:cache"]);

                Response.Headers.Append("X-Cache-Hit", "1");
                return File(cachedReplay, content_type, fileName);
            }

            var replayStream = await replayStorage.GetReplayStreamAsync(score.id);

            DogStatsd.Increment("replays_downloaded", tags: ["source:storage"]);

            Response.Headers.Append("X-Cache-Hit", "0");
            return File(replayStream, content_type, fileName);
        }

        /// <summary>
        /// Deletes the replay for a score.
        /// </summary>
        /// <response code="204">The replay was deleted successfully.</response>
        /// <response code="404">The given score ID could not be found in the database, or the score has no replay.</response>
        [HttpDelete]
        [Route("{scoreId}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DeleteReplayAsync([FromRoute] ulong scoreId)
        {
            Score? score;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                score = await db.GetScoreAsync(scoreId);
            }

            if (score == null || !score.has_replay)
                return NotFound();

            await replayStorage.DeleteReplayAsync(scoreId);
            await replayCache.RemoveAsync(scoreId);

            DogStatsd.Increment("replays_deleted");

            return NoContent();
        }

        private static async Task<Stream> createLegacyReplayWithHeadersAsync(byte[] frames, ushort rulesetId, Score score, MySqlConnection db)
        {
            var user = await db.GetUserAsync(score.user_id);
            Debug.Assert(user != null);

            var beatmap = await db.GetBeatmapAsync(score.beatmap_id);
            Debug.Assert(beatmap != null);

            int? scoreVersion = await db.GetLegacyScoreVersionAsync(score.legacy_score_id!.Value, rulesetId);

            var replayWithHeaders = LegacyReplayHelper.WriteReplayWithHeader(
                frames,
                rulesetId,
                scoreVersion,
                score,
                user,
                beatmap);

            return replayWithHeaders;
        }

        private static string createFileName(ulong scoreId, uint beatmapId, ushort rulesetId)
        {
            string ruleset = LegacyRulesetHelper.GetRulesetNameFromLegacyId(rulesetId);

            return $"solo-replay-{ruleset}_{beatmapId}_{scoreId}.osr";
        }
    }
}
