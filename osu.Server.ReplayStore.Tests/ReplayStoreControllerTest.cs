// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net;
using Dapper;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using osu.Framework.Extensions;
using osu.Game.Rulesets.Scoring;
using osu.Server.QueueProcessor;
using osu.Server.ReplayStore.Helpers;
using osu.Server.ReplayStore.Models.Database;
using osu.Server.ReplayStore.Services;
using osu.Server.ReplayStore.Tests.Resources;

namespace osu.Server.ReplayStore.Tests
{
    public class ReplayStoreControllerTest : IntegrationTest
    {
        private const string solo_replay_filename = "solo-replay.osr";
        private const string legacy_replay_filename = "legacy-replay.osr";

        protected new HttpClient Client { get; }

        private readonly LocalReplayStorage replayStorage;
        private readonly FileReplayCache replayCache;

        public ReplayStoreControllerTest(IntegrationTestWebApplicationFactory<Program> webApplicationFactory)
            : base(webApplicationFactory)
        {
            replayStorage = new LocalReplayStorage(
                Directory.CreateTempSubdirectory(nameof(ReplayStoreControllerTest)).FullName);

            replayCache = new FileReplayCache(
                Directory.CreateTempSubdirectory($"{nameof(ReplayStoreControllerTest)}_cache").FullName);

            Client = webApplicationFactory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddTransient<IReplayStorage>(_ => replayStorage);
                    services.AddTransient<IReplayCache>(_ => replayCache);
                });
            }).CreateClient();
        }

        [Fact]
        public async Task TestPutReplay_NewReplay()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`) values (1, 1, 0, 1, '{}', now());");

            using var stream = TestResources.GetResource(solo_replay_filename)!;
            byte[] replayBytes = await stream.ReadAllRemainingBytesToArrayAsync();

            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(replayBytes), "replayFile", solo_replay_filename);

            var response = await Client.PutAsync("/replays/1", form);
            Assert.True(response.IsSuccessStatusCode);

            byte[]? cachedReplay = await replayCache.FindReplayDataAsync(scoreId: 1);
            Assert.NotNull(cachedReplay);
            Assert.True(cachedReplay.Length > 0);
            Assert.Equal(replayBytes, cachedReplay);

            using var storedReplayStream = await replayStorage.GetReplayStreamAsync(scoreId: 1);
            byte[] storedReplay = await storedReplayStream.ReadAllRemainingBytesToArrayAsync();
            Assert.NotNull(storedReplay);
            Assert.True(storedReplay.Length > 0);
            Assert.Equal(replayBytes, storedReplay);
        }

        [Fact]
        public async Task TestPutReplay_FailsIfNoScore()
        {
            using var stream = TestResources.GetResource(solo_replay_filename)!;

            var form = new MultipartFormDataContent();
            form.Add(new StreamContent(stream), "replayFile", solo_replay_filename);

            var response = await Client.PutAsync("/replays/1", form);
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TestPutReplay_NewLegacyReplay()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            var date = DateTimeOffset.UtcNow.Date;

            var scoreData = new SoloScoreData
            {
                Statistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Great] = 1,
                    [HitResult.Ok] = 0,
                    [HitResult.Meh] = 0,
                    [HitResult.Miss] = 0,
                },
                MaximumStatistics = new Dictionary<HitResult, int>
                {
                    [HitResult.Great] = 1,
                    [HitResult.Ok] = 0,
                    [HitResult.Meh] = 0,
                    [HitResult.Miss] = 0,
                },
            };

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`, `legacy_score_id`, `rank`, `has_replay`) values (1, 1, 0, 1, @Data, @Date, 123, 'S', 1);",
                new { Date = date, Data = JsonConvert.SerializeObject(scoreData) });

            await db.ExecuteAsync(
                "INSERT INTO `phpbb_users` (`user_id`, `username`, `username_clean`, `country_acronym`, `user_permissions`, `user_sig`, `user_occ`, `user_interests`) VALUES (1, 'test', 'test', 'JP', '', '', '', '')");

            await db.ExecuteAsync(
                "INSERT INTO `osu_replays` (`score_id`) VALUES (1);");

            await db.ExecuteAsync(
                "INSERT INTO `osu_beatmaps` (`beatmap_id`, `checksum`) VALUES (1, '5d370b1b0483f4fc7c64bff0ade06c0f');");

            using var stream = TestResources.GetResource(legacy_replay_filename)!;
            byte[] replayBytes = await stream.ReadAllRemainingBytesToArrayAsync();

            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(replayBytes), "replayFile", legacy_replay_filename);

            using var replayWithHeader = LegacyReplayHelper.WriteReplayWithHeader(
                replayBytes,
                rulesetId: 0,
                scoreVersion: null,
                new Score
                {
                    id = 1,
                    user_id = 1,
                    beatmap_id = 1,
                    ruleset_id = 0,
                    has_replay = true,
                    ended_at = date,
                    rank = "S",
                    legacy_score_id = 123,
                    ScoreData = scoreData,
                },
                new User
                {
                    username = "test",
                },
                new OsuBeatmap
                {
                    checksum = "5d370b1b0483f4fc7c64bff0ade06c0f",
                });

            byte[] replayWithHeaderBytes = await replayWithHeader.ReadAllRemainingBytesToArrayAsync();

            var response = await Client.PutAsync("/replays/1", form);
            Assert.True(response.IsSuccessStatusCode);

            byte[]? cachedReplay = await replayCache.FindReplayDataAsync(scoreId: 1);
            Assert.NotNull(cachedReplay);
            Assert.True(cachedReplay.Length > 0);
            Assert.Equal(replayWithHeaderBytes, cachedReplay);

            using var storedReplayStream = await replayStorage.GetReplayStreamAsync(scoreId: 1);
            byte[] storedReplay = await storedReplayStream.ReadAllRemainingBytesToArrayAsync();
            Assert.NotNull(storedReplay);
            Assert.True(storedReplay.Length > 0);
            Assert.Equal(replayWithHeaderBytes, storedReplay);
        }

        [Fact]
        public async Task TestGetReplay_SendsReplay()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`, `has_replay`) values (1, 1, 0, 1, '{}', now(), 1);");

            using var stream = TestResources.GetResource(solo_replay_filename)!;
            byte[] replayBytes = await stream.ReadAllRemainingBytesToArrayAsync();

            stream.Seek(0, SeekOrigin.Begin);

            await replayStorage.StoreReplayAsync(scoreId: 1, stream);

            var response = await Client.GetAsync("/replays/1");
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("0", response.Headers.GetValues("X-Cache-Hit").Single());

            byte[] responseReplay = await response.Content.ReadAsByteArrayAsync();

            Assert.True(responseReplay.Length > 0);
            Assert.Equal(replayBytes, responseReplay);
        }

        [Fact]
        public async Task TestGetReplay_CachedReplay_SendsReplayFromCache()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`, `has_replay`) values (1, 1, 0, 1, '{}', now(), 1);");

            using var stream = TestResources.GetResource(solo_replay_filename)!;
            byte[] replayBytes = await stream.ReadAllRemainingBytesToArrayAsync();

            await replayCache.AddAsync(scoreId: 1, replayBytes);

            var response = await Client.GetAsync("/replays/1");
            Assert.True(response.IsSuccessStatusCode);
            Assert.Equal("1", response.Headers.GetValues("X-Cache-Hit").Single());

            byte[] responseReplay = await response.Content.ReadAsByteArrayAsync();

            Assert.True(responseReplay.Length > 0);
            Assert.Equal(replayBytes, responseReplay);
        }

        [Fact]
        public async Task TestGetReplay_FailsIfNoScore()
        {
            var response = await Client.GetAsync("/replays/1");
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TestGetReplay_FailsIfNoReplay()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`, `has_replay`) values (1, 1, 0, 1, '{}', now(), 0);");

            var response = await Client.GetAsync("/replays/1");
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TestDeleteReplay_DeletesReplay()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`, `has_replay`) values (1, 1, 0, 1, '{}', now(), 1);");

            using var stream = TestResources.GetResource(solo_replay_filename)!;
            byte[] replayData = await stream.ReadAllRemainingBytesToArrayAsync();

            stream.Seek(0, SeekOrigin.Begin);

            await replayStorage.StoreReplayAsync(scoreId: 1, stream);
            await replayCache.AddAsync(scoreId: 1, replayData);

            var response = await Client.DeleteAsync("/replays/1");
            Assert.True(response.IsSuccessStatusCode);

            byte[]? cachedReplay = await replayCache.FindReplayDataAsync(scoreId: 1);
            Assert.Null(cachedReplay);

            await Assert.ThrowsAsync<FileNotFoundException>(() => replayStorage.GetReplayStreamAsync(scoreId: 1));
        }

        [Fact]
        public async Task TestDeleteReplay_FailsIfNoScore()
        {
            var response = await Client.DeleteAsync("/replays/1");
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task TestDeleteReplay_FailsIfNoReplay()
        {
            using var db = await DatabaseAccess.GetConnectionAsync();

            await db.ExecuteAsync(
                "INSERT INTO `scores` (`id`, `user_id`, `ruleset_id`, `beatmap_id`, `data`, `ended_at`, `has_replay`) values (1, 1, 0, 1, '{}', now(), 0);");

            var response = await Client.DeleteAsync("/replays/1");
            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
