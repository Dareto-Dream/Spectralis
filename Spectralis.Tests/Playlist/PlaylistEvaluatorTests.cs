using System;
using System.Collections.Generic;
using Spectralis.Core.Library;
using Spectralis.Core.Playlists;
using Xunit;

namespace Spectralis.Tests.Playlist
{
    public class PlaylistEvaluatorTests
    {
        private static LibraryTrack MakeTrack(string artist = "A", string genre = "Rock", int year = 2020, int rating = 3)
            => new LibraryTrack { Artist = artist, Genre = genre, Year = year, Rating = rating, DurationSeconds = 200 };

        [Fact]
        public void Evaluate_ArtistRule_FiltersCorrectly()
        {
            var tracks = new List<LibraryTrack> { MakeTrack("Radiohead"), MakeTrack("Blur") };
            var rule = new SmartPlaylistRule { Field = "artist", Operator = "equals", Value = "Radiohead" };
            var evaluator = new SmartPlaylistEvaluator();
            var result = evaluator.Evaluate(tracks, new[] { rule });
            Assert.Single(result);
            Assert.Equal("Radiohead", result[0].Artist);
        }

        [Fact]
        public void Evaluate_GenreRule_FiltersCorrectly()
        {
            var tracks = new List<LibraryTrack> { MakeTrack(genre: "Jazz"), MakeTrack(genre: "Rock") };
            var rule = new SmartPlaylistRule { Field = "genre", Operator = "equals", Value = "Jazz" };
            var evaluator = new SmartPlaylistEvaluator();
            var result = evaluator.Evaluate(tracks, new[] { rule });
            Assert.Single(result);
        }

        [Fact]
        public void Evaluate_YearRule_GreaterThan()
        {
            var tracks = new List<LibraryTrack> { MakeTrack(year: 2018), MakeTrack(year: 2022) };
            var rule = new SmartPlaylistRule { Field = "year", Operator = "greater_than", Value = "2020" };
            var evaluator = new SmartPlaylistEvaluator();
            var result = evaluator.Evaluate(tracks, new[] { rule });
            Assert.Single(result);
            Assert.Equal(2022, result[0].Year);
        }

        [Fact]
        public void Evaluate_RatingRule_FiltersAboveThreshold()
        {
            var tracks = new List<LibraryTrack>
            {
                MakeTrack(rating: 2),
                MakeTrack(rating: 4),
                MakeTrack(rating: 5)
            };
            var rule = new SmartPlaylistRule { Field = "rating", Operator = "greater_than", Value = "3" };
            var evaluator = new SmartPlaylistEvaluator();
            var result = evaluator.Evaluate(tracks, new[] { rule });
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Evaluate_NoRules_ReturnsAll()
        {
            var tracks = new List<LibraryTrack> { MakeTrack(), MakeTrack(), MakeTrack() };
            var evaluator = new SmartPlaylistEvaluator();
            var result = evaluator.Evaluate(tracks, Array.Empty<SmartPlaylistRule>());
            Assert.Equal(3, result.Count);
        }
    }
}
