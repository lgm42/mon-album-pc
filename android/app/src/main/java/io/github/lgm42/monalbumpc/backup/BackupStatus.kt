package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.data.media.MediaEntity
import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.net.ApiError

/** What the worker does after a failed call (protocol §6). */
enum class FailureAction {
    /** The PC cannot be reached: keep the items pending and retry later. */
    RETRY_LATER,

    /** The PC's destination disk is unplugged: stop, keep pending, show the banner. */
    STOP_DESTINATION_UNAVAILABLE,

    /** The PC's disk is full: stop, keep pending, notify. */
    STOP_DISK_FULL,

    /** A USB import of this phone is running: stop quietly. */
    STOP_BUSY,

    /** The PC no longer knows this phone: forget the pairing. */
    UNPAIR,

    /** The app is too old for the PC: stop until it is updated. */
    STOP_PROTOCOL,

    /** Resend from the offset the PC expects. */
    RESUME_AT_OFFSET,

    /** The upload is gone (hash mismatch, expired): declare the file again. */
    RESTART_UPLOAD,

    /** Only this item failed: mark it as an error and go on. */
    FAIL_ITEM,

    /** The PC got this file but could not store it: go on with the others, try it again in a later run. */
    SKIP_ITEM,
}

object BackupErrors {

    fun actionFor(error: ApiError): FailureAction = when (error) {
        is ApiError.Network, ApiError.CertificateMismatch, ApiError.InvalidResponse -> FailureAction.RETRY_LATER
        ApiError.DestinationUnavailable -> FailureAction.STOP_DESTINATION_UNAVAILABLE
        ApiError.DiskFull -> FailureAction.STOP_DISK_FULL
        ApiError.BusyUsb -> FailureAction.STOP_BUSY
        ApiError.DeviceUnknown -> FailureAction.UNPAIR
        ApiError.ProtocolUnsupported -> FailureAction.STOP_PROTOCOL
        is ApiError.OffsetMismatch -> FailureAction.RESUME_AT_OFFSET
        ApiError.HashMismatch -> FailureAction.RESTART_UPLOAD
        is ApiError.ImportFailed -> FailureAction.SKIP_ITEM
        is ApiError.Http -> when {
            error.status == 404 -> FailureAction.RESTART_UPLOAD
            error.status == 401 -> FailureAction.UNPAIR
            error.status >= 500 || error.status == 408 || error.status == 429 -> FailureAction.RETRY_LATER
            else -> FailureAction.FAIL_ITEM
        }
        ApiError.PairingInvalid, ApiError.PairingExpired -> FailureAction.FAIL_ITEM
    }

    /**
     * Errors while sending one file. A bare 500 comes from a server older than `import_failed` that crashed on this
     * very file: treated the same way, so that the run does not stop on it again and again.
     */
    fun forUpload(error: ApiError): ApiError =
        if (error is ApiError.Http && error.status == 500) ApiError.ImportFailed(null) else error
}

/**
 * Files the server could not store are tried again later, less and less often, so that one bad file neither blocks
 * the backup nor costs a full upload at every run.
 */
object RetryPolicy {

    private val delaysMinutes = longArrayOf(15, 60, 4 * 60, 12 * 60, 24 * 60)

    /** After a run where [refusedInARow] files were refused one after the other, the server is probably failing on all. */
    const val MAX_REFUSED_IN_A_ROW = 3

    /** When to try again a file that failed [failures] times (1 for the first failure). */
    fun nextAttemptMs(failures: Int, nowMs: Long): Long =
        nowMs + delaysMinutes[(failures - 1).coerceIn(0, delaysMinutes.lastIndex)] * 60_000L

    fun isDue(item: MediaEntity, nowMs: Long): Boolean = item.retryAfter.let { it == null || it <= nowMs }

    /** The items of this run: everything not saved, except failed files whose next attempt has not come yet. */
    fun due(items: List<MediaEntity>, nowMs: Long): List<MediaEntity> = items.filter { isDue(it, nowMs) }
}

/** Situation left by the last backup run, persisted for the banner. */
enum class BackupIssue { NONE, PC_UNREACHABLE, DESTINATION_UNAVAILABLE, DISK_FULL }

/** What a running backup is doing, published by the worker. */
enum class BackupPhase { SEARCHING, COMPARING, PREPARING, UPLOADING }

/** Step of a running backup, with the file at hand and its bytes when it is being sent. */
data class RunActivity(
    val phase: BackupPhase,
    val fileName: String? = null,
    val fileBytes: Long = 0,
    val fileSize: Long = 0,
    val bytesPerSecond: Long = 0,
) {
    /** Share of the current file sent (0 to 1), or null outside of an upload. */
    val fileFraction: Float?
        get() = if (phase == BackupPhase.UPLOADING && fileSize > 0) (fileBytes.toFloat() / fileSize).coerceIn(0f, 1f) else null

    /** Seconds left for the current file at the current pace, or null when unknown. */
    val secondsLeft: Long?
        get() = if (phase == BackupPhase.UPLOADING && bytesPerSecond > 0 && fileSize > fileBytes) {
            (fileSize - fileBytes + bytesPerSecond - 1) / bytesPerSecond
        } else {
            null
        }
}

/** The most recent file in error, for the banner: "Le NAS a refusé VID-….mp4 : …". */
data class FailedFile(val name: String, val reason: String?, val message: String?, val retryAfterMs: Long?)

/** Variants of the status banner of the Photos screen (design README §2). */
sealed interface BannerState {
    data class Running(val done: Int, val total: Int, val activity: RunActivity) : BannerState {
        /** Rank of the file at hand, 1-based: "Envoi 4 / 20" while the fourth file goes. */
        val position: Int
            get() = if (total > 0) (done + 1).coerceAtMost(total) else 0
    }
    data object DestinationUnavailable : BannerState
    data object DiskFull : BannerState
    data class PcUnreachable(val sinceMs: Long?, val address: String? = null) : BannerState
    data class Errors(val count: Int, val latest: FailedFile? = null) : BannerState
    data class Pending(val count: Int) : BannerState
    data object Empty : BannerState
    data class AllSaved(val lastBackupMs: Long?, val checkedMs: Long? = null) : BannerState
}

object BannerLogic {

    fun compute(
        running: Boolean,
        done: Int,
        total: Int,
        issue: BackupIssue,
        issueSinceMs: Long?,
        errors: Int,
        pending: Int,
        trackedCount: Int,
        lastBackupMs: Long?,
        activity: RunActivity? = null,
        issueDetail: String? = null,
        latestError: FailedFile? = null,
        checkedMs: Long? = null,
    ): BannerState {
        if (running && activity != null) return BannerState.Running(done.coerceIn(0, total.coerceAtLeast(0)), total, activity)
        val waiting = pending + errors > 0
        if (waiting) {
            when (issue) {
                BackupIssue.DESTINATION_UNAVAILABLE -> return BannerState.DestinationUnavailable
                BackupIssue.DISK_FULL -> return BannerState.DiskFull
                BackupIssue.PC_UNREACHABLE -> return BannerState.PcUnreachable(issueSinceMs, issueDetail)
                BackupIssue.NONE -> Unit
            }
        }
        if (errors > 0) return BannerState.Errors(errors, latestError)
        if (pending > 0) return BannerState.Pending(pending)
        if (trackedCount == 0) return BannerState.Empty
        return BannerState.AllSaved(lastBackupMs, checkedMs)
    }

    /** The file in error to name in the banner: the one that failed last, i.e. whose next attempt is the latest. */
    fun latestError(items: List<MediaEntity>): FailedFile? = items
        .filter { it.state == MediaState.ERROR }
        .maxByOrNull { it.retryAfter ?: Long.MIN_VALUE }
        ?.let { FailedFile(it.displayName, it.errorReason, it.errorMessage, it.retryAfter) }
}

/** Coarse elapsed time for "il y a 2 h", "depuis 3 jours". */
sealed interface Elapsed {
    data object JustNow : Elapsed
    data class Minutes(val count: Int) : Elapsed
    data class Hours(val count: Int) : Elapsed
    data class Days(val count: Int) : Elapsed

    companion object {
        fun between(thenMs: Long, nowMs: Long): Elapsed {
            val minutes = ((nowMs - thenMs).coerceAtLeast(0) / 60_000L)
            return when {
                minutes < 1 -> JustNow
                minutes < 60 -> Minutes(minutes.toInt())
                minutes < 24 * 60 -> Hours((minutes / 60).toInt())
                else -> Days((minutes / (24 * 60)).toInt())
            }
        }
    }
}
