package io.github.lgm42.monalbumpc.data.media

import androidx.room.Dao
import androidx.room.Entity
import androidx.room.Insert
import androidx.room.PrimaryKey
import androidx.room.Query
import androidx.room.Update
import kotlinx.coroutines.flow.Flow

/** How a backup run ended, for the history of the Sauvegarde tab. */
enum class RunEnd {
    COMPLETED,
    UNREACHABLE,
    DESTINATION_UNAVAILABLE,
    DISK_FULL,

    /** Several files refused in a row: the server fails on everything for now. */
    REFUSED,

    /** Connection lost, or stopped by Android. */
    INTERRUPTED,
    PAUSED,

    /** A USB import of this phone is running on the server, or the app is too old for it. */
    BUSY,
    ;

    companion object {
        fun parse(value: String): RunEnd = entries.firstOrNull { it.name == value } ?: INTERRUPTED
    }
}

/** One backup run (or several in a row that ended the same way without moving a file). Times are epoch ms. */
@Entity(tableName = "backup_runs")
data class BackupRunEntity(
    @PrimaryKey(autoGenerate = true) val id: Long = 0,
    val startedAt: Long,
    val endedAt: Long,
    /** Files waiting when the run started. */
    val queued: Int,
    val sent: Int,
    /** Files the server already had (sent before, imported by USB…). */
    val recognized: Int,
    val failed: Int,
    val sentBytes: Long,
    /** A [RunEnd] name. */
    val outcome: String,
    /** Runs merged in this row. */
    val attempts: Int,
    /** E.g. the address that did not answer. */
    val detail: String?,
)

val BackupRunEntity.end: RunEnd
    get() = RunEnd.parse(outcome)

/** Nothing sent, recognized or failed. */
val BackupRunEntity.movedNothing: Boolean
    get() = sent == 0 && recognized == 0 && failed == 0

@Dao
interface BackupRunDao {

    @Query("SELECT * FROM backup_runs ORDER BY startedAt DESC, id DESC LIMIT :limit")
    fun observeRecent(limit: Int): Flow<List<BackupRunEntity>>

    @Query("SELECT * FROM backup_runs ORDER BY startedAt DESC, id DESC LIMIT 1")
    suspend fun latest(): BackupRunEntity?

    @Insert
    suspend fun insert(run: BackupRunEntity): Long

    @Update
    suspend fun update(run: BackupRunEntity)

    @Query("DELETE FROM backup_runs WHERE id NOT IN (SELECT id FROM backup_runs ORDER BY startedAt DESC, id DESC LIMIT :keep)")
    suspend fun prune(keep: Int)

    @Query("DELETE FROM backup_runs")
    suspend fun deleteAll()
}
