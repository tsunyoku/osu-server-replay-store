// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Tests.Beatmaps;
using osu.Server.ReplayStore.Helpers;
using osu.Server.ReplayStore.Models.Database;
using osu.Server.ReplayStore.Tests.Resources;

namespace osu.Server.ReplayStore.Tests
{
    public class LegacyReplayHelperTest
    {
        private const string legacy_replay_filename = "legacy-replay.osr";
        private const int legacy_replay_version = 20250628;

        [Fact]
        public async Task WriteReplayWithHeader_WritesValidHeader()
        {
            using var stream = TestResources.GetResource(legacy_replay_filename)!;

            var score = new Score
            {
                id = 1,
                legacy_score_id = 4501250208,
                legacy_total_score = 13160096,
                ScoreData = new SoloScoreData
                {
                    Statistics = new Dictionary<HitResult, int>
                    {
                        [HitResult.Great] = 525,
                        [HitResult.Ok] = 3,
                        [HitResult.Meh] = 0,
                        [HitResult.Miss] = 0,
                    },
                    Mods = [new APIMod { Acronym = "DT" }]
                },
                max_combo = 724,
                user_id = 11315329,
                ended_at = new DateTimeOffset(2023, 09, 04, 21, 10, 42, TimeSpan.Zero),
                rank = "S",
                has_replay = true,
            };

            var user = new User
            {
                username = "tsunyoku"
            };

            var beatmap = new OsuBeatmap
            {
                checksum = "5d370b1b0483f4fc7c64bff0ade06c0f"
            };

            var response = LegacyReplayHelper.WriteReplayWithHeader(
                await stream.ReadAllBytesToArrayAsync(),
                rulesetId: 0,
                legacy_replay_version,
                score,
                user,
                beatmap);

            var scoreDecoder = new TestLegacyScoreDecoder();

            var decodedScore = scoreDecoder.Parse(response);

            Assert.Equal(user.username, decodedScore.ScoreInfo.RealmUser.Username);
            Assert.Equal(score.ScoreData.Statistics[HitResult.Great], decodedScore.ScoreInfo.GetCount300());
            Assert.Equal(score.ScoreData.Statistics[HitResult.Ok], decodedScore.ScoreInfo.GetCount100());
            Assert.Equal(score.ScoreData.Statistics[HitResult.Meh], decodedScore.ScoreInfo.GetCount50());
            Assert.Equal(score.ScoreData.Statistics[HitResult.Miss], decodedScore.ScoreInfo.GetCountMiss());
            Assert.Equal(score.legacy_total_score, decodedScore.ScoreInfo.LegacyTotalScore);
            Assert.Equal(score.max_combo, (uint)decodedScore.ScoreInfo.MaxCombo);
            Assert.Equal(score.ended_at.DateTime, decodedScore.ScoreInfo.Date);
            Assert.Equal(score.legacy_score_id, (ulong)decodedScore.ScoreInfo.LegacyOnlineID);
        }
    }

    public class TestLegacyScoreDecoder : LegacyScoreDecoder
    {
        protected override Ruleset GetRuleset(int rulesetId) => new OsuRuleset();

        protected override WorkingBeatmap GetBeatmap(string md5Hash) => new TestWorkingBeatmap(new Beatmap
        {
            BeatmapInfo = new BeatmapInfo
            {
                MD5Hash = md5Hash,
                Ruleset = new OsuRuleset().RulesetInfo,
                Difficulty = new BeatmapDifficulty(),
            },
            // needs to have at least one object so that `StandardisedScoreMigrationTools` doesn't die
            // when trying to recompute total score.
            HitObjects =
            {
                new HitCircle()
            },
            BeatmapVersion = 14,
        });
    }
}
