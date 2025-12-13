// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions;
using osu.Game.Beatmaps.Legacy;
using osu.Game.IO.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Taiko;
using osu.Server.ReplayStore.Models.Database;

namespace osu.Server.ReplayStore.Helpers
{
    public static class LegacyReplayHelper
    {
        private const int default_replay_version = 20151228;

        private static readonly Dictionary<ushort, Ruleset> rulesets = new Dictionary<ushort, Ruleset>()
        {
            { 0, new OsuRuleset() },
            { 1, new TaikoRuleset() },
            { 2, new CatchRuleset() },
            { 3, new ManiaRuleset() },
        };

        public static Stream WriteReplayWithHeader(byte[] frameData, ushort rulesetId, int? scoreVersion, Score score, User user, OsuBeatmap beatmap)
        {
            var memoryStream = new MemoryStream();

            using var writer = new SerializationWriter(memoryStream, leaveOpen: true);

            string scoreChecksum = $"{score.max_combo}osu{user.username}{beatmap.checksum}{score.legacy_total_score}{score.rank}";

            LegacyMods legacyMods = rulesets[rulesetId].ConvertToLegacyMods(
                score.ScoreData.Mods.Select(x => x.ToMod(rulesets[rulesetId])).ToArray());

            // header section
            writer.Write((byte)rulesetId);
            writer.Write(scoreVersion ?? default_replay_version);
            writer.Write(beatmap.checksum);
            writer.Write(user.username);
            writer.Write(scoreChecksum.ComputeMD5Hash());
            writer.Write((ushort)score.ScoreData.Statistics[HitResult.Great]);
            writer.Write((ushort)score.ScoreData.Statistics[HitResult.Ok]);
            writer.Write((ushort)score.ScoreData.Statistics[HitResult.Meh]);
            writer.Write((ushort)0); // geki
            writer.Write((ushort)0); // katu
            writer.Write((ushort)score.ScoreData.Statistics[HitResult.Miss]);
            writer.Write((int)score.legacy_total_score);
            writer.Write((ushort)score.max_combo);
            writer.Write(score.max_combo == score.ScoreData.MaximumStatistics.Where(kvp => kvp.Key.AffectsCombo()).Sum(kvp => kvp.Value));
            writer.Write((int)legacyMods);

            writer.Write(string.Empty); // empty hp bar
            writer.Write(score.ended_at.DateTime);

            writer.WriteByteArray(frameData);
            writer.Write(score.legacy_score_id!.Value);

            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        }
    }
}
