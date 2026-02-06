// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Dapper;
using MySqlConnector;
using osu.Server.ReplayStore.Helpers;
using osu.Server.ReplayStore.Models.Database;

namespace osu.Server.ReplayStore
{
    public static class DatabaseOperationExtensions
    {
        public static Task<Score?> GetScoreAsync(this MySqlConnection db, ulong scoreId, MySqlTransaction? transaction = null)
        {
            return db.QuerySingleOrDefaultAsync<Score?>(@"SELECT * FROM `scores` WHERE `id` = @scoreId",
                new
                {
                    scoreId
                },
                transaction: transaction);
        }

        public static Task<User?> GetUserAsync(this MySqlConnection db, uint userId, MySqlTransaction? transaction = null)
        {
            return db.QuerySingleOrDefaultAsync<User?>(@"SELECT * FROM `phpbb_users` WHERE `user_id` = @userId",
                new
                {
                    userId
                },
                transaction: transaction);
        }

        public static Task<OsuBeatmap?> GetBeatmapAsync(this MySqlConnection db, uint beatmapId, MySqlTransaction? transaction = null)
        {
            return db.QuerySingleOrDefaultAsync<OsuBeatmap?>(@"SELECT * FROM `osu_beatmaps` WHERE `beatmap_id` = @beatmapId",
                new
                {
                    beatmapId
                },
                transaction: transaction);
        }

        public static Task<int?> GetLegacyScoreVersionAsync(this MySqlConnection db, ulong legacyScoreId, ushort rulesetId, MySqlTransaction? transaction = null)
        {
            string replayViewCountTable = LegacyRulesetHelper.GetLegacyReplayViewCountTableFromLegacyId(rulesetId);

            return db.QuerySingleOrDefaultAsync<int?>(@$"SELECT `version` FROM `{replayViewCountTable}` WHERE `score_id` = @legacyScoreId",
                new
                {
                    legacyScoreId = legacyScoreId
                },
                transaction: transaction);
        }
    }
}
