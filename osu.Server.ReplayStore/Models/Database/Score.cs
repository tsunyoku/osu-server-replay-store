// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// ReSharper disable InconsistentNaming

using Newtonsoft.Json;

namespace osu.Server.ReplayStore.Models.Database
{
    public class Score
    {
        public ulong id { get; set; }

        public uint user_id { get; set; }

        public uint beatmap_id { get; set; }

        public ushort ruleset_id { get; set; }

        public uint max_combo { get; set; }

        public uint legacy_total_score { get; set; }

        public string rank { get; set; } = null!;

        public DateTimeOffset ended_at { get; set; }

        public bool has_replay { get; set; }

        public ulong? legacy_score_id { get; set; }

        public bool IsLegacyScore => legacy_score_id.HasValue;

        public SoloScoreData ScoreData = new SoloScoreData();

        public string data
        {
            get => JsonConvert.SerializeObject(ScoreData);
            set
            {
                var soloScoreData = JsonConvert.DeserializeObject<SoloScoreData>(value);
                if (soloScoreData != null)
                    ScoreData = soloScoreData;
            }
        }
    }
}
