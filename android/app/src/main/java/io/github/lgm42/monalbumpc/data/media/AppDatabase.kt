package io.github.lgm42.monalbumpc.data.media

import android.content.Context
import androidx.room.Database
import androidx.room.Room
import androidx.room.RoomDatabase
import androidx.room.TypeConverters
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

@Database(entities = [MediaEntity::class], version = 2, exportSchema = false)
@TypeConverters(MediaConverters::class)
abstract class AppDatabase : RoomDatabase() {

    abstract fun mediaDao(): MediaDao

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

        fun get(context: Context): AppDatabase =
            instance ?: synchronized(this) {
                instance ?: Room.databaseBuilder(context.applicationContext, AppDatabase::class.java, "monalbumpc.db")
                    .addMigrations(MIGRATION_1_2)
                    // The index can always be rebuilt from MediaStore and POST /check.
                    .fallbackToDestructiveMigration()
                    .build()
                    .also { instance = it }
            }
    }
}
