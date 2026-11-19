package io.github.lgm42.monalbumpc.data.media

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.TypeConverters
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

@Database(entities = [MediaEntity::class, BackupRunEntity::class], version = 3, exportSchema = false)
@TypeConverters(MediaConverters::class)
abstract class AppDatabase : RoomDatabase() {

    abstract fun mediaDao(): MediaDao

    abstract fun backupRunDao(): BackupRunDao

    companion object {
        @Volatile
        private var instance: AppDatabase? = null

        /** Retry bookkeeping of failed files. Kept by a migration: rebuilding the index means hashing everything again. */
        val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("ALTER TABLE media ADD COLUMN failures INTEGER NOT NULL DEFAULT 0")
                db.execSQL("ALTER TABLE media ADD COLUMN retryAfter INTEGER")
                db.execSQL("ALTER TABLE media ADD COLUMN errorMessage TEXT")
            }
        }

        /** History of the backup runs; the index is kept. Same statement as the one Room generates for [BackupRunEntity]. */
        val MIGRATION_2_3 = object : Migration(2, 3) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL(CREATE_BACKUP_RUNS)
            }
        }

        const val CREATE_BACKUP_RUNS = "CREATE TABLE IF NOT EXISTS `backup_runs` (" +
            "`id` INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL, `startedAt` INTEGER NOT NULL, `endedAt` INTEGER NOT NULL, " +
            "`queued` INTEGER NOT NULL, `sent` INTEGER NOT NULL, `recognized` INTEGER NOT NULL, `failed` INTEGER NOT NULL, " +
            "`sentBytes` INTEGER NOT NULL, `outcome` TEXT NOT NULL, `attempts` INTEGER NOT NULL, `detail` TEXT)"

        fun get(context: Context): AppDatabase =
            instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(context.applicationContext, AppDatabase::class.java, "monalbumpc.db")
                    .addMigrations(MIGRATION_1_2, MIGRATION_2_3)
                    // The index can always be rebuilt from MediaStore and POST /check.
                    .fallbackToDestructiveMigration()
                    .build()
                    .also { instance = it }
            }
    }
}
