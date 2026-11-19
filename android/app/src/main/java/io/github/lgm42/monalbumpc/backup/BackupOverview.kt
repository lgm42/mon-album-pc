package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.data.media.MediaEntity
import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceFolder
import io.github.lgm42.monalbumpc.media.DeviceMedia
import io.github.lgm42.monalbumpc.media.FolderRules

/** A number of files and their size. */
data class Tally(val count: Int = 0, val bytes: Long = 0) {
    operator fun plus(size: Long) = Tally(count + 1, bytes + size)
}

/** "Appareil photo 1 608 / 1 610": how far a tracked folder is saved. */
data class FolderProgress(
    val key: String,
    val name: String,
    val saved: Int,
    val total: Int,
    val failed: Int,
    val newestUri: String?,
) {
    val fraction: Float
        get() = if (total > 0) saved.toFloat() / total else 1f
}

/** A folder the backup leaves out, with how many photos and videos it holds. */
data class UntrackedFolder(val key: String, val name: String, val count: Int)

/** A file still to send, as listed in "À venir". */
data class QueuedFile(
    val id: Long,
    val name: String,
    val uri: String,
    val size: Long,
    val isVideo: Boolean,
    val folderName: String,
    val dateMs: Long,
    val durationMs: Long = 0,
)

/** A file the last attempt could not save. */
data class FailedEntry(
    val id: Long,
    val name: String,
    val uri: String,
    val size: Long,
    val isVideo: Boolean,
    val reason: String?,
    /** What the server said, shown as is. */
    val message: String?,
    val retryAfterMs: Long?,
    val failures: Int,
    val durationMs: Long = 0,
)

/** A file saved on the server, for "Récemment sauvegardées". */
data class SavedEntry(
    val id: Long,
    val name: String,
    val uri: String,
    val size: Long,
    val isVideo: Boolean,
    val savedAtMs: Long,
    val durationMs: Long = 0,
)

/** Everything the Sauvegarde tab counts and lists, from MediaStore and the local index. */
data class BackupSummary(
    val saved: Tally = Tally(),
    val pending: Tally = Tally(),
    val failed: Tally = Tally(),
    val folders: List<FolderProgress> = emptyList(),
    val untracked: List<UntrackedFolder> = emptyList(),
    /** Next files to send, in the order of the backup (newest first). */
    val queue: List<QueuedFile> = emptyList(),
    /** Failed files, the next to be tried first. */
    val failures: List<FailedEntry> = emptyList(),
    /** Last saved files, most recent first. */
    val recent: List<SavedEntry> = emptyList(),
) {
    val tracked: Int
        get() = saved.count + pending.count + failed.count

    /** When the first failed file will be tried again by itself. */
    val nextRetryMs: Long?
        get() = failures.mapNotNull { it.retryAfterMs }.minOrNull()
}

/** Files and bytes of the running backup, for the overall bar. */
data class RunTotals(
    val doneFiles: Int,
    val totalFiles: Int,
    val doneBytes: Long,
    val totalBytes: Long,
    /** How fast the queue goes down, all steps included; 0 while unknown. */
    val bytesPerSecond: Long = 0,
) {
    /** Share of the run done (by bytes when known, else by files), 0 to 1. */
    val fraction: Float
        get() = when {
            totalBytes > 0 -> (doneBytes.toFloat() / totalBytes).coerceIn(0f, 1f)
            totalFiles > 0 -> (doneFiles.toFloat() / totalFiles).coerceIn(0f, 1f)
            else -> 0f
        }

    /** Seconds left for the whole run at the current pace, or null when unknown. */
    val secondsLeft: Long?
        get() = if (bytesPerSecond > 0 && totalBytes > doneBytes) {
            (totalBytes - doneBytes + bytesPerSecond - 1) / bytesPerSecond
        } else {
            null
        }
}

/** The big state line of the Sauvegarde tab, most urgent first. */
sealed interface BackupHeadline {
    /** [activity] is null while the run reads the phone's photos, before it needs the server. */
    data class Running(val done: Int, val total: Int, val activity: RunActivity?, val run: RunTotals?) : BackupHeadline {
        /** Rank of the file at hand, 1-based. */
        val position: Int
            get() = if (total > 0) (done + 1).coerceAtMost(total) else 0
    }
    /** No usable network: Wi-Fi only, or any connection when [mobileDataAllowed]. */
    data class WaitingNetwork(val mobileDataAllowed: Boolean) : BackupHeadline
    data object WaitingCharging : BackupHeadline
    data class Unreachable(val sinceMs: Long?, val address: String?, val nextAttemptMs: Long?) : BackupHeadline
    data object DestinationUnavailable : BackupHeadline
    data object DiskFull : BackupHeadline
    data class Failures(val count: Int, val nextRetryMs: Long?) : BackupHeadline
    data class Pending(val count: Int, val nextRunMs: Long?) : BackupHeadline
    data class AllSaved(val checkedMs: Long?) : BackupHeadline
    data object Empty : BackupHeadline
}

/** What the phone allows right now, as WorkManager sees it for the backup constraints. */
data class DeviceConditions(val connected: Boolean = true, val unmetered: Boolean = true, val charging: Boolean = false) {
    fun networkOk(mobileDataAllowed: Boolean): Boolean = if (mobileDataAllowed) connected else unmetered
}

object BackupOverview {

    /**
     * Counts every photo and video of the tracked folders as saved, failed or pending (not indexed yet counts as
     * pending, like the gallery badges), per folder too, and lists the queue, the failures and the last saved files.
     */
    fun summarize(
        media: List<DeviceMedia>,
        folders: List<DeviceFolder>,
        tracked: Set<String>,
        index: List<MediaEntity>,
        recentLimit: Int = RECENT_LIMIT,
    ): BackupSummary {
        val byId = HashMap<Long, MediaEntity>(index.size * 2)
        index.forEach { byId[it.mediaStoreId] = it }
        val folderNames = HashMap<String, String>()
        folders.forEach { folderNames[it.key] = it.friendlyName }

        var saved = Tally()
        var pending = Tally()
        var failed = Tally()
        val perFolder = HashMap<String, IntArray>() // saved, total, failed
        val queue = ArrayList<QueuedFile>()
        val failures = ArrayList<FailedEntry>()
        val recent = ArrayList<SavedEntry>()

        for (m in media) {
            val key = m.folderKey
            if (key !in tracked) continue
            val entity = byId[m.id]
            val counts = perFolder.getOrPut(key) { IntArray(3) }
            counts[1]++
            when (entity?.state) {
                MediaState.SAVED -> {
                    saved += m.size
                    counts[0]++
                    entity.savedAt?.let { recent.add(SavedEntry(m.id, m.displayName, m.uri, m.size, m.isVideo, it, m.durationMs)) }
                }
                MediaState.ERROR -> {
                    failed += m.size
                    counts[2]++
                    failures.add(
                        FailedEntry(
                            id = m.id,
                            name = m.displayName,
                            uri = m.uri,
                            size = m.size,
                            isVideo = m.isVideo,
                            reason = entity.errorReason,
                            message = entity.errorMessage,
                            retryAfterMs = entity.retryAfter,
                            failures = entity.failures,
                            durationMs = m.durationMs,
                        ),
                    )
                }
                else -> {
                    pending += m.size
                    val folderName = folderNames[key] ?: FolderRules.classify(m.relativePath, m.volume).friendlyName
                    queue.add(QueuedFile(m.id, m.displayName, m.uri, m.size, m.isVideo, folderName, m.sortDateMs, m.durationMs))
                }
            }
        }

        val folderRows = folders.filter { it.key in tracked }.map { folder ->
            val counts = perFolder[folder.key] ?: IntArray(3)
            FolderProgress(folder.key, folder.friendlyName, counts[0], counts[1], counts[2], folder.newestUri)
        }
        val untracked = folders.filter { it.key !in tracked }.map { UntrackedFolder(it.key, it.friendlyName, it.count) }

        return BackupSummary(
            saved = saved,
            pending = pending,
            failed = failed,
            folders = folderRows,
            untracked = untracked,
            // Same order as the worker: newest first.
            queue = queue.sortedWith(compareByDescending<QueuedFile> { it.dateMs }.thenByDescending { it.id }),
            failures = failures.sortedWith(compareBy<FailedEntry> { it.retryAfterMs ?: Long.MIN_VALUE }.thenBy { it.name }),
            recent = recent.sortedWith(compareByDescending<SavedEntry> { it.savedAtMs }.thenByDescending { it.id })
                .take(recentLimit),
        )
    }

    /** The state to show in big, the most useful first: what runs, what blocks, what waits. */
    fun headline(
        summary: BackupSummary,
        running: Boolean,
        done: Int,
        total: Int,
        activity: RunActivity?,
        run: RunTotals?,
        conditions: DeviceConditions,
        chargingOnly: Boolean,
        mobileData: Boolean,
        issue: BackupIssue,
        issueSinceMs: Long?,
        issueDetail: String?,
        checkedMs: Long?,
        nextRunMs: Long?,
    ): BackupHeadline {
        val waiting = summary.pending.count + summary.failed.count
        // A run that only reads the phone's photos is shown when there is something to send, not at every check.
        if (running && (activity != null || waiting > 0)) return BackupHeadline.Running(done, total, activity, run)
        if (waiting == 0) {
            return if (summary.tracked == 0) BackupHeadline.Empty else BackupHeadline.AllSaved(checkedMs)
        }
        when (issue) {
            BackupIssue.DESTINATION_UNAVAILABLE -> return BackupHeadline.DestinationUnavailable
            BackupIssue.DISK_FULL -> return BackupHeadline.DiskFull
            else -> Unit
        }
        if (!conditions.networkOk(mobileData)) return BackupHeadline.WaitingNetwork(mobileData)
        if (chargingOnly && !conditions.charging) return BackupHeadline.WaitingCharging
        if (issue == BackupIssue.PC_UNREACHABLE) return BackupHeadline.Unreachable(issueSinceMs, issueDetail, nextRunMs)
        if (summary.pending.count > 0) return BackupHeadline.Pending(summary.pending.count, nextRunMs)
        return BackupHeadline.Failures(summary.failed.count, summary.nextRetryMs)
    }

    const val RECENT_LIMIT = 200
}
