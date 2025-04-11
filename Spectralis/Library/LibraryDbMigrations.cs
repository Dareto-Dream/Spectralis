using System;

namespace Spectralis.Library
{
    public static class LibraryDbMigrations
    {
        private const int CurrentVersion = 2;

        public static void Apply(LibraryDb db)
        {
            int version = GetVersion(db);

            if (version < 1)
            {
                db.Execute(LibrarySchema.CreateFtsIndex);
                SetVersion(db, 1);
            }

            if (version < 2)
            {
                db.Execute("ALTER TABLE tracks ADD COLUMN rating INTEGER DEFAULT 0");
                SetVersion(db, 2);
            }
        }

        private static int GetVersion(LibraryDb db)
        {
            try
            {
                db.Execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)");
                var result = db.QueryScalar("SELECT version FROM schema_version LIMIT 1");
                return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
            }
            catch { return 0; }
        }

        private static void SetVersion(LibraryDb db, int version)
        {
            db.Execute("DELETE FROM schema_version");
            db.Execute("INSERT INTO schema_version (version) VALUES (@v)", ("@v", version));
        }
    }
}
