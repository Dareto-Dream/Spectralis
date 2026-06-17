using System;
using System.Collections.Generic;
using System.Linq;
using Spectralis.Core.Models;

namespace Spectralis.Core.Playlists
{
    public class SmartPlaylistEvaluator
    {
        public IReadOnlyList<TrackInfo> Evaluate(Playlist playlist, IEnumerable<TrackInfo> library)
        {
            IEnumerable<TrackInfo> filtered = library;

            if (playlist.IsSmart && playlist.Rules.Count > 0)
            {
                filtered = playlist.MatchAll
                    ? library.Where(t => playlist.Rules.All(r => Matches(t, r)))
                    : library.Where(t => playlist.Rules.Any(r => Matches(t, r)));
            }

            filtered = playlist.SortBy switch
            {
                "artist" => filtered.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber),
                "album" => filtered.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber),
                "title" => filtered.OrderBy(t => t.Title),
                "duration" => filtered.OrderBy(t => t.Duration),
                "random" => filtered.OrderBy(_ => Guid.NewGuid()),
                _ => filtered.OrderBy(t => t.Artist)
            };

            if (playlist.Limit.HasValue)
                filtered = filtered.Take(playlist.Limit.Value);

            return filtered.ToList();
        }

        private static bool Matches(TrackInfo track, SmartPlaylistRule rule)
        {
            string fieldValue = rule.Field switch
            {
                SmartRuleField.Title => track.Title,
                SmartRuleField.Artist => track.Artist,
                SmartRuleField.Album => track.Album,
                SmartRuleField.Genre => track.Genre,
                SmartRuleField.Year => track.Year.ToString(),
                SmartRuleField.Duration => ((int)track.Duration.TotalSeconds).ToString(),
                _ => ""
            };

            string ruleVal = rule.Value;

            return rule.Operator switch
            {
                SmartRuleOperator.Contains => fieldValue.Contains(ruleVal, StringComparison.OrdinalIgnoreCase),
                SmartRuleOperator.NotContains => !fieldValue.Contains(ruleVal, StringComparison.OrdinalIgnoreCase),
                SmartRuleOperator.Equals => string.Equals(fieldValue, ruleVal, StringComparison.OrdinalIgnoreCase),
                SmartRuleOperator.StartsWith => fieldValue.StartsWith(ruleVal, StringComparison.OrdinalIgnoreCase),
                SmartRuleOperator.GreaterThan => double.TryParse(fieldValue, out var fv) && double.TryParse(ruleVal, out var rv) && fv > rv,
                SmartRuleOperator.LessThan => double.TryParse(fieldValue, out var fv2) && double.TryParse(ruleVal, out var rv2) && fv2 < rv2,
                _ => false
            };
        }
    }
}
