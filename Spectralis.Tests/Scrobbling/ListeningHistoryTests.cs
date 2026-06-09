using System;
using System.Collections.Generic;
using Spectralis.Core.Scrobbling;
using Xunit;

namespace Spectralis.Tests.Scrobbling
{
    public class ListeningHistoryTests
    {
        [Fact]
        public void History_AddEntry_IncreasesCount()
        {
            var history = new ListeningHistory();
            history.Add(new ListeningHistoryEntry { Title = "Song", Artist = "Artist", ScrobbledAt = DateTimeOffset.UtcNow });
            Assert.Equal(1, history.Entries.Count);
        }

        [Fact]
        public void History_MaxEntries_TrimsOldest()
        {
            var history = new ListeningHistory(maxEntries: 3);
            for (int i = 0; i < 5; i++)
                history.Add(new ListeningHistoryEntry { Title = $"Song {i}", Artist = "A", ScrobbledAt = DateTimeOffset.UtcNow });
            Assert.Equal(3, history.Entries.Count);
        }

        [Fact]
        public void History_Entries_OrderedNewestFirst()
        {
            var history = new ListeningHistory();
            history.Add(new ListeningHistoryEntry { Title = "Old", Artist = "A", ScrobbledAt = DateTimeOffset.UtcNow.AddHours(-1) });
            history.Add(new ListeningHistoryEntry { Title = "New", Artist = "A", ScrobbledAt = DateTimeOffset.UtcNow });
            Assert.Equal("New", history.Entries[0].Title);
        }

        [Fact]
        public void History_Clear_RemovesAll()
        {
            var history = new ListeningHistory();
            history.Add(new ListeningHistoryEntry { Title = "X", Artist = "Y", ScrobbledAt = DateTimeOffset.UtcNow });
            history.Clear();
            Assert.Empty(history.Entries);
        }
    }
}
