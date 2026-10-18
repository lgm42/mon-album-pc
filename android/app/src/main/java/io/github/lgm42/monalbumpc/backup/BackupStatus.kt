package io.github.lgm42.monalbumpc.backup

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
        is ApiError.Http -> when {
            error.status == 404 -> FailureAction.RESTART_UPLOAD
            error.status == 401 -> FailureAction.UNPAIR
            error.status >= 500 || error.status == 408 || error.status == 429 -> FailureAction.RETRY_LATER
            else -> FailureAction.FAIL_ITEM
        }
        ApiError.PairingInvalid, ApiError.PairingExpired -> FailureAction.FAIL_ITEM
    }
}

/** Situation left by the last backup run, persisted for the banner. */
enum class BackupIssue { NONE, PC_UNREACHABLE, DESTINATION_UNAVAILABLE, DISK_FULL }

/** Variants of the status banner of the Photos screen (design README §2). */
sealed interface BannerState {
    data class Running(val done: Int, val total: Int) : BannerState
    data object DestinationUnavailable : BannerState
    data object DiskFull : BannerState
    data class PcUnreachable(val sinceMs: Long?) : BannerState
    data class Errors(val count: Int) : BannerState
    data class Pending(val count: Int) : BannerState
    data object Empty : BannerState
    data class AllSaved(val lastBackupMs: Long?) : BannerState
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
    ): BannerState {
        if (running && total > 0) return BannerState.Running(done.coerceIn(0, total), total)
        val waiting = pending + errors > 0
        if (waiting) {
            when (issue) {
                BackupIssue.DESTINATION_UNAVAILABLE -> return BannerState.DestinationUnavailable
                BackupIssue.DISK_FULL -> return BannerState.DiskFull
                BackupIssue.PC_UNREACHABLE -> return BannerState.PcUnreachable(issueSinceMs)
                BackupIssue.NONE -> Unit
            }
        }
        if (errors > 0) return BannerState.Errors(errors)
        if (pending > 0) return BannerState.Pending(pending)
        if (trackedCount == 0) return BannerState.Empty
        return BannerState.AllSaved(lastBackupMs)
    }
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
