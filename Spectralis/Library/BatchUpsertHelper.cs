using System;
using System.Collections.Generic;

namespace Spectralis.Library
{
    public static class BatchUpsertHelper
    {
        public static void UpsertAll(LibraryDb db, LibraryRepository repo, IEnumerable<LibraryTrack> tracks)
        {
            using var tx = db.BeginTransaction();
            try
            {
                foreach (var track in tracks)
                    repo.Upsert(track);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
