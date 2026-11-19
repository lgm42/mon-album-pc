package io.github.lgm42.monalbumpc.backup

import androidx.room.withTransaction
import io.github.lgm42.monalbumpc.data.media.AppDatabase
import io.github.lgm42.monalbumpc.data.media.BackupRunEntity
import io.github.lgm42.monalbumpc.data.media.movedNothing

/** The last backup runs, kept on the phone for the Sauvegarde tab. */
object RunHistory {

    /** Runs kept; older ones are dropped. */
    const val KEEP = 100

    /**
     * The row to write for [next]: runs that end the same way without moving a file (the server off for the night,
     * retried every 15 minutes) extend the previous row instead of filling the history.
     */
    fun merge(previous: BackupRunEntity?, next: BackupRunEntity): BackupRunEntity =
        if (previous != null && previous.outcome == next.outcome && previous.movedNothing && next.movedNothing) {
            previous.copy(
                endedAt = next.endedAt,
                queued = next.queued,
                attempts = previous.attempts + next.attempts,
                detail = next.detail ?: previous.detail,
            )
        } else {
            next
        }

    suspend fun record(db: AppDatabase, run: BackupRunEntity) {
        db.withTransaction {
            val dao = db.backupRunDao()
            val row = merge(dao.latest(), run)
            if (row.id == 0L) dao.insert(row) else dao.update(row)
            dao.prune(KEEP)
        }
    }
}
