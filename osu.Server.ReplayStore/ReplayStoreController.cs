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
            [FromRoute] long scoreId,
            IFormFile replayFile)
        {
            Score? score;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                score = await db.GetScoreAsync(scoreId);
            }

            if (score == null)
                return NotFound();

            var replayStream = replayFile.OpenReadStream();

            await replayStorage.StoreReplayAsync(
                (long?)score.legacy_score_id ?? scoreId,
                score.ruleset_id,
                score.IsLegacyScore,
                replayStream);

            replayStream.Seek(0, SeekOrigin.Begin);

            await sendReplayToCache(replayStream, score);

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
        public async Task<IActionResult> GetReplayAsync([FromRoute] long scoreId)
        {
            Score? score;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                score = await db.GetScoreAsync(scoreId);
            }

            if (score == null || !score.has_replay)
                return NotFound();

            string fileName = createFileName(scoreId, score.beatmap_id, score.ruleset_id, legacyScore: score.IsLegacyScore);

            byte[]? cachedReplay = await replayCache.FindReplayDataAsync(scoreId);

            if (cachedReplay != null)
            {
                DogStatsd.Increment("replays_downloaded", tags: ["source:cache"]);

                Response.Headers.Append("X-Cache-Hit", "1");
                return File(cachedReplay, content_type, fileName);
            }

            var replayStream = await getReplayFromStorage(score);

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
        [Route("{scoreId:long}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> DeleteReplayAsync([FromRoute] long scoreId)
        {
            Score? score;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                score = await db.GetScoreAsync(scoreId);
            }

            if (score == null || !score.has_replay)
                return NotFound();

            await replayStorage.DeleteReplayAsync(
                (long?)score.legacy_score_id ?? scoreId,
                score.ruleset_id,
                score.IsLegacyScore);

            await replayCache.RemoveAsync(scoreId);

            DogStatsd.Increment("replays_deleted");

            return NoContent();
        }

        /// <summary>
        /// Sends the replay for a given score to the <see cref="IReplayCache"/>.
        /// </summary>
        /// <remarks>
        /// For legacy scores, this will append replay headers to the given stream before storage.
        /// This ensures all replays stored to the cache contain headers, and can be immediately returned on fetch.
        /// </remarks>
        /// <param name="replayStream">The replay stream.</param>
        /// <param name="score">The score.</param>
        private async Task sendReplayToCache(Stream replayStream, Score score)
        {
            byte[] replayBytes = await replayStream.ReadAllRemainingBytesToArrayAsync();

            await replayStream.DisposeAsync();

            if (score.IsLegacyScore)
            {
                Stream replayWithHeadersStream;

                using (var db = await DatabaseAccess.GetConnectionAsync())
                {
                    replayWithHeadersStream = await createLegacyReplayWithHeadersAsync(
                        replayBytes,
                        score.ruleset_id,
                        score,
                        db);
                }

                replayBytes = await replayWithHeadersStream.ReadAllRemainingBytesToArrayAsync();
            }

            await replayCache.AddAsync((long)score.id, replayBytes);
        }

        /// <summary>
        /// Retrieves the replay for a given score from storage.
        /// </summary>
        /// <remarks>
        /// For legacy scores, this will append headers onto the retrieved replay as legacy scores are stored with only the frame data.
        /// </remarks>
        /// <param name="score">The score.</param>
        /// <returns>The replay.</returns>
        private async Task<Stream> getReplayFromStorage(Score score)
        {
            var replayStream = await replayStorage.GetReplayStreamAsync(
                (long?)score.legacy_score_id ?? (long)score.id,
                score.ruleset_id,
                score.IsLegacyScore);

            byte[] replayBytes = await replayStream.ReadAllRemainingBytesToArrayAsync();

            replayStream.Seek(0, SeekOrigin.Begin);

            if (!score.IsLegacyScore)
                return replayStream;

            Stream replayWithHeadersStream;

            using (var db = await DatabaseAccess.GetConnectionAsync())
            {
                replayWithHeadersStream = await createLegacyReplayWithHeadersAsync(
                    replayBytes,
                    score.ruleset_id,
                    score,
                    db);
            }

            return replayWithHeadersStream;
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

        private static string createFileName(long scoreId, uint beatmapId, ushort rulesetId, bool legacyScore)
        {
            string ruleset = LegacyRulesetHelper.GetRulesetNameFromLegacyId(rulesetId);

            string replayType = legacyScore ? "replay" : "solo-replay";

            return $"{replayType}-{ruleset}_{beatmapId}_{scoreId}.osr";
        }
    }
}
