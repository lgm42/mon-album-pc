package io.github.lgm42.monalbumpc.backup

import android.content.Context
import android.net.Uri
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.ForegroundInfo
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import io.github.lgm42.monalbumpc.data.BackupPrefs
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.data.PairingStore
import io.github.lgm42.monalbumpc.data.media.AppDatabase
import io.github.lgm42.monalbumpc.data.media.ErrorReasons
import io.github.lgm42.monalbumpc.data.media.MediaEntity
import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceFolders
import io.github.lgm42.monalbumpc.media.FolderRules
import io.github.lgm42.monalbumpc.media.FolderStore
import io.github.lgm42.monalbumpc.media.MediaStoreReader
import io.github.lgm42.monalbumpc.net.ApiError
import io.github.lgm42.monalbumpc.net.ApiException
import io.github.lgm42.monalbumpc.net.CheckItem
import io.github.lgm42.monalbumpc.net.CheckRequest
import io.github.lgm42.monalbumpc.net.PcApi
import io.github.lgm42.monalbumpc.net.PcDiscovery
import io.github.lgm42.monalbumpc.net.StatusResponse
import io.github.lgm42.monalbumpc.net.UploadRequest
import java.io.IOException
import java.io.InputStream
import java.util.concurrent.atomic.AtomicLong
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.mapNotNull
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Saves the photos and videos of the tracked folders on the paired PC (cdc §7, protocol §5).
 * Runs in the foreground (data sync) while transferring; nothing stays at rest.
 */
class BackupWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {

    private val notifications = BackupNotifications(appContext)
    private val pairingStore = PairingStore(appContext)
    private val prefs = BackupPrefs(appContext)
    private val folderStore = FolderStore(appContext)
    private val dao = AppDatabase.get(appContext).mediaDao()
    private val reader = MediaStoreReader(appContext)
    private val resolver = appContext.contentResolver

    // Progress shared with the ticker that publishes it.
    @Volatile private var done = 0
    @Volatile private var total = 0
    @Volatile private var currentId = -1L
    @Volatile private var currentSize = 0L
    private val currentBytes = AtomicLong(0)

    private var uploadedPhotos = 0
    private var uploadedVideos = 0

    override suspend fun getForegroundInfo(): ForegroundInfo {
        val name = pairingStore.paired.first()?.pcName.orEmpty()
        return notifications.progressInfo(0, 0, name)
    }

    override suspend fun doWork(): Result {
        // Periodic, content-triggered and manual runs never upload at the same time.
        val result = runLock.withLock {
            try {
                runBackup()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                dao.resetUploading()
                Result.retry()
            }
        }
        // A retried run comes back by itself and re-arms the trigger then.
        if (result != Result.retry()) rearmContentTrigger()
        return result
    }

    private suspend fun rearmContentTrigger() {
        if (pairingStore.paired.first() == null) return
        val chargingOnly = prefs.current().chargingOnly
        // The content-triggered work queues its successor; the others only make sure one exists.
        val policy = if (tags.contains(BackupScheduler.TAG_CONTENT)) {
            ExistingWorkPolicy.APPEND_OR_REPLACE
        } else {
            ExistingWorkPolicy.KEEP
        }
        BackupScheduler.armContentTrigger(applicationContext, chargingOnly, policy)
    }

    private suspend fun runBackup(): Result {
        val paired = pairingStore.paired.first() ?: return Result.success()
        dao.resetUploading()

        // 1. Local index of the tracked folders.
        syncIndex()
        val queue = dao.unsavedOldestFirst()
        if (queue.isEmpty()) {
            prefs.setIssue(BackupIssue.NONE, System.currentTimeMillis())
            return Result.success()
        }

        // 2. Reach the PC.
        val connection = when (val c = connect(paired)) {
            is Connection.Connected -> c
            Connection.Unreachable -> {
                prefs.setIssue(BackupIssue.PC_UNREACHABLE, System.currentTimeMillis())
                catchUpLater()
                return Result.success()
            }
            Connection.Busy -> return Result.success()
            Connection.Unpaired -> {
                forgetPc()
                return Result.success()
            }
        }
        if (connection.status.busy == "usb") return Result.success()
        val pc = connection.pc
        val api = connection.api

        total = queue.size
        done = 0
        setForegroundSafely(notifications.progressInfo(0, total, pc.pcName))

        return coroutineScope {
            val ticker = launch {
                var lastDone = -1
                while (isActive) {
                    publishProgress()
                    if (done != lastDone) {
                        lastDone = done
                        setForegroundSafely(notifications.progressInfo(done, total, pc.pcName))
                    }
                    delay(PROGRESS_INTERVAL_MS)
                }
            }
            try {
                val outcome = processQueue(pc, api, connection.status, queue)
                finish(pc, outcome)
            } finally {
                ticker.cancel()
            }
        }
    }

    /** Hashes, checks and uploads the queue in chunks, oldest first. */
    private suspend fun processQueue(
        pc: PairedPc,
        api: PcApi,
        status: StatusResponse,
        queue: List<MediaEntity>,
    ): RunOutcome {
        for (chunk in queue.chunked(CHUNK_SIZE)) {
            if (isStopped) return RunOutcome.Retry

            // 3. SHA-256 of the original bytes.
            val hashed = ArrayList<MediaEntity>(chunk.size)
            for (item in chunk) {
                if (isStopped) return RunOutcome.Retry
                val withHash = if (item.sha256 != null) item else hash(item)
                if (withHash == null) {
                    done++
                } else {
                    hashed.add(withHash)
                }
            }

            // 4. What the PC already has (history, USB imports, destination index).
            val have: Set<String> = if (hashed.isEmpty()) emptySet() else try {
                api.check(pc.authorization, CheckRequest(hashed.map { CheckItem(it.sha256!!, it.size) })).have.toSet()
            } catch (e: ApiException) {
                when (val stop = stopFor(e.error)) {
                    null -> emptySet()
                    else -> return stop
                }
            }
            val already = hashed.filter { it.sha256 in have }
            if (already.isNotEmpty()) {
                dao.markSaved(already.map { it.mediaStoreId }, System.currentTimeMillis())
                done += already.size
            }

            if (!status.destinationAvailable) return RunOutcome.Stop(BackupIssue.DESTINATION_UNAVAILABLE)

            // 5. Upload the rest, one file at a time.
            for (item in hashed) {
                if (item.sha256 in have) continue
                if (isStopped) return RunOutcome.Retry
                when (val result = upload(pc, api, item)) {
                    ItemResult.Saved, ItemResult.Failed, ItemResult.Skipped -> done++
                    is ItemResult.Abort -> return result.outcome
                }
            }
        }
        return RunOutcome.Completed
    }

    private suspend fun finish(pc: PairedPc, outcome: RunOutcome): Result {
        val now = System.currentTimeMillis()
        publishProgress()
        if (uploadedPhotos + uploadedVideos > 0) {
            prefs.setLastBackup(now)
            notifications.notifyDone(uploadedPhotos, uploadedVideos, pc.pcName)
        }
        return when (outcome) {
            RunOutcome.Completed -> {
                if (!isCatchUp) BackupScheduler.cancelCatchUp(applicationContext)
                prefs.setLastBackup(now)
                prefs.setIssue(BackupIssue.NONE, now)
                notifications.cancelIssue()
                Result.success()
            }
            is RunOutcome.Stop -> {
                prefs.setIssue(outcome.issue, now)
                val state = prefs.current()
                if (outcome.issue != BackupIssue.NONE && state.notifiedIssue != outcome.issue) {
                    notifications.notifyIssue(outcome.issue, pc.pcName)
                    prefs.setNotifiedIssue(outcome.issue)
                }
                Result.success()
            }
            RunOutcome.Quiet -> Result.success()
            // Stopped by the system: WorkManager brings this run back. Connection lost: retry on our own schedule.
            RunOutcome.Retry -> if (isStopped) {
                Result.retry()
            } else {
                catchUpLater()
                Result.success()
            }
            RunOutcome.Unpaired -> {
                forgetPc()
                Result.success()
            }
        }
    }

    private val isCatchUp: Boolean
        get() = tags.contains(BackupScheduler.TAG_CATCH_UP)

    private suspend fun catchUpLater() {
        val attempt = inputData.getInt(BackupScheduler.KEY_ATTEMPT, 0)
        BackupScheduler.scheduleCatchUp(applicationContext, attempt, prefs.current().chargingOnly, isCatchUp)
    }

    /** The reset cancels this very work: finish it whatever happens. */
    private suspend fun forgetPc() {
        withContext(NonCancellable) { BackupReset.forgetPc(applicationContext) }
    }

    // region Index

    private suspend fun syncIndex() {
        val media = reader.readAll()
        // Nothing visible usually means the photo permission was withdrawn: keep the index.
        if (media.isEmpty()) return
        val folders = DeviceFolders.fromMedia(media)
        val tracked = folderStore.trackedOrDefaults(folders)
        val diff = IndexSync.diff(dao.getAll(), media, tracked)
        if (diff.deletions.isNotEmpty()) diff.deletions.chunked(SQL_CHUNK).forEach { dao.deleteIds(it) }
        if (diff.upserts.isNotEmpty()) diff.upserts.chunked(SQL_CHUNK).forEach { dao.upsert(it) }
    }

    /** Returns the item with its hash, or null if it cannot be read (marked as an error). */
    private suspend fun hash(item: MediaEntity): MediaEntity? {
        val sha = try {
            withContext(Dispatchers.IO) {
                open(item).use { Sha256.hex(it) }
            }
        } catch (e: IOException) {
            null
        } catch (e: SecurityException) {
            null
        } catch (e: IllegalArgumentException) {
            null
        }
        if (sha == null) {
            dao.markError(item.mediaStoreId, ErrorReasons.UNREADABLE)
            return null
        }
        dao.setHash(item.mediaStoreId, sha)
        return item.copy(sha256 = sha)
    }

    private fun open(item: MediaEntity): InputStream =
        resolver.openInputStream(Uri.parse(item.uri)) ?: throw IOException("Cannot open ${item.uri}")

    // endregion

    // region Upload

    private suspend fun upload(pc: PairedPc, api: PcApi, start: MediaEntity): ItemResult {
        var item = start
        val folderName = FolderRules.classify(item.relativePath, item.volume).friendlyName
        var hashFailures = 0
        var restarts = 0
        currentId = item.mediaStoreId
        currentSize = item.size
        currentBytes.set(0)
        try {
            declare@ while (true) {
                val request = UploadRequest(
                    sha256 = item.sha256!!,
                    size = item.size,
                    name = item.displayName,
                    sourceFolder = folderName,
                    mime = item.mime,
                    takenAt = IsoDates.formatOrNull(item.dateTaken),
                    modifiedAt = IsoDates.formatOrNull(item.dateModified),
                )
                val declared = try {
                    api.createUpload(pc.authorization, request)
                } catch (e: ApiException) {
                    if (e.error == ApiError.HashMismatch) {
                        // The PC already had every byte of an interrupted upload, and they were wrong.
                        hashFailures++
                        if (hashFailures >= MAX_HASH_FAILURES) return failItem(item, ErrorReasons.HASH_MISMATCH)
                        item = rehash(item) ?: return ItemResult.Failed
                        continue@declare
                    }
                    throw e
                }
                when (declared.status) {
                    STATUS_EXISTS, STATUS_SAVED -> return saved(item)
                    STATUS_UPLOAD -> Unit
                    else -> return failItem(item, ErrorReasons.SERVER)
                }
                val uploadId = declared.uploadId ?: return failItem(item, ErrorReasons.SERVER)
                var offset = (declared.offset ?: 0L).coerceIn(0L, item.size)
                dao.markUploading(item.mediaStoreId, uploadId)

                var stalls = 0
                while (true) {
                    if (isStopped) return ItemResult.Abort(RunOutcome.Retry)
                    currentBytes.set(offset)
                    val body = OffsetStreamBody(item.size, offset, { open(item) }, { currentBytes.set(it) })
                    try {
                        val answer = api.patchUpload(pc.authorization, uploadId, offset, body)
                        if (answer.status == STATUS_SAVED || answer.status == STATUS_EXISTS || answer.offset >= item.size) {
                            return saved(item)
                        }
                        if (answer.offset <= offset) stalls++
                        if (stalls > MAX_STALLS) return keepPending(item)
                        offset = answer.offset.coerceIn(0L, item.size)
                    } catch (e: ApiException) {
                        when (BackupErrors.actionFor(e.error)) {
                            FailureAction.RESUME_AT_OFFSET -> {
                                stalls++
                                if (stalls > MAX_STALLS) return keepPending(item)
                                val expected = (e.error as? ApiError.OffsetMismatch)?.expected
                                    ?: api.uploadOffset(pc.authorization, uploadId)
                                if (expected == null) {
                                    restarts++
                                    if (restarts > MAX_RESTARTS) return keepPending(item)
                                    continue@declare
                                }
                                offset = expected.coerceIn(0L, item.size)
                            }
                            FailureAction.RESTART_UPLOAD -> {
                                if (e.error == ApiError.HashMismatch) {
                                    hashFailures++
                                    if (hashFailures >= MAX_HASH_FAILURES) return failItem(item, ErrorReasons.HASH_MISMATCH)
                                    // The file may have changed since it was hashed.
                                    item = rehash(item) ?: return ItemResult.Failed
                                } else {
                                    restarts++
                                    if (restarts > MAX_RESTARTS) return keepPending(item)
                                }
                                continue@declare
                            }
                            else -> throw e
                        }
                    }
                }
            }
        } catch (e: ApiException) {
            return when (BackupErrors.actionFor(e.error)) {
                FailureAction.RETRY_LATER -> abort(item, RunOutcome.Retry, null)
                FailureAction.STOP_DESTINATION_UNAVAILABLE ->
                    abort(item, RunOutcome.Stop(BackupIssue.DESTINATION_UNAVAILABLE), ErrorReasons.DESTINATION_UNAVAILABLE)
                FailureAction.STOP_DISK_FULL -> abort(item, RunOutcome.Stop(BackupIssue.DISK_FULL), ErrorReasons.DISK_FULL)
                FailureAction.STOP_BUSY, FailureAction.STOP_PROTOCOL -> abort(item, RunOutcome.Quiet, null)
                FailureAction.UNPAIR -> ItemResult.Abort(RunOutcome.Unpaired)
                FailureAction.FAIL_ITEM, FailureAction.RESUME_AT_OFFSET, FailureAction.RESTART_UPLOAD ->
                    failItem(item, ErrorReasons.SERVER)
            }
        } finally {
            currentId = -1L
        }
    }

    private suspend fun rehash(item: MediaEntity): MediaEntity? = hash(item.copy(sha256 = null))

    private suspend fun saved(item: MediaEntity): ItemResult {
        dao.markSaved(listOf(item.mediaStoreId), System.currentTimeMillis())
        if (item.isVideo) uploadedVideos++ else uploadedPhotos++
        return ItemResult.Saved
    }

    private suspend fun failItem(item: MediaEntity, reason: String): ItemResult {
        dao.markError(item.mediaStoreId, reason)
        return ItemResult.Failed
    }

    private suspend fun keepPending(item: MediaEntity): ItemResult {
        dao.markPending(item.mediaStoreId, null)
        return ItemResult.Skipped
    }

    private suspend fun abort(item: MediaEntity, outcome: RunOutcome, reason: String?): ItemResult {
        if (item.state == MediaState.ERROR) {
            dao.markError(item.mediaStoreId, reason ?: item.errorReason ?: ErrorReasons.SERVER)
        } else {
            dao.markPending(item.mediaStoreId, reason)
        }
        return ItemResult.Abort(outcome)
    }

    /** Errors of POST /check that end the run; null to go on without its answer. */
    private fun stopFor(error: ApiError): RunOutcome? = when (BackupErrors.actionFor(error)) {
        FailureAction.RETRY_LATER -> RunOutcome.Retry
        FailureAction.STOP_DESTINATION_UNAVAILABLE -> RunOutcome.Stop(BackupIssue.DESTINATION_UNAVAILABLE)
        FailureAction.STOP_DISK_FULL -> RunOutcome.Stop(BackupIssue.DISK_FULL)
        FailureAction.STOP_BUSY, FailureAction.STOP_PROTOCOL -> RunOutcome.Quiet
        FailureAction.UNPAIR -> RunOutcome.Unpaired
        else -> null
    }

    // endregion

    // region Connection

    private sealed interface Connection {
        data class Connected(val pc: PairedPc, val api: PcApi, val status: StatusResponse) : Connection
        data object Unreachable : Connection
        data object Busy : Connection
        data object Unpaired : Connection
    }

    /** Tries the last known address, then looks the PC up by its id on the network (protocol §2). */
    private suspend fun connect(pc: PairedPc): Connection {
        val first = status(pc)
        if (first != Connection.Unreachable) return first
        val found = withTimeoutOrNull(DISCOVERY_TIMEOUT_MS) {
            PcDiscovery(applicationContext).discover()
                .mapNotNull { list -> list.firstOrNull { it.pcId == pc.pcId } }
                .first()
        } ?: return first
        if (found.host == pc.host && found.port == pc.apiPort) return first
        val moved = pc.copy(host = found.host, apiPort = found.port)
        val second = status(moved)
        if (second is Connection.Connected) pairingStore.save(moved)
        return second
    }

    private suspend fun status(pc: PairedPc): Connection = try {
        val api = PcApi(pc.host, pc.apiPort, pc.certSha256)
        Connection.Connected(pc, api, api.status(pc.authorization))
    } catch (e: ApiException) {
        when (BackupErrors.actionFor(e.error)) {
            FailureAction.UNPAIR -> Connection.Unpaired
            FailureAction.STOP_BUSY, FailureAction.STOP_PROTOCOL -> Connection.Busy
            else -> Connection.Unreachable
        }
    }

    // endregion

    private suspend fun publishProgress() {
        val size = currentSize
        val percent = if (size > 0) ((currentBytes.get() * 100) / size).toInt().coerceIn(0, 100) else 0
        setProgress(
            workDataOf(
                KEY_DONE to done,
                KEY_TOTAL to total,
                KEY_CURRENT_ID to currentId,
                KEY_CURRENT_PERCENT to percent,
            ),
        )
    }

    /** Foreground start can be refused from the background (Android 12+): the work goes on anyway. */
    private suspend fun setForegroundSafely(info: ForegroundInfo) {
        try {
            setForeground(info)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // Not allowed now; the work continues as a regular job.
        }
    }

    private sealed interface RunOutcome {
        data object Completed : RunOutcome
        data class Stop(val issue: BackupIssue) : RunOutcome
        data object Quiet : RunOutcome
        data object Retry : RunOutcome
        data object Unpaired : RunOutcome
    }

    private sealed interface ItemResult {
        data object Saved : ItemResult
        data object Failed : ItemResult
        data object Skipped : ItemResult
        data class Abort(val outcome: RunOutcome) : ItemResult
    }

    companion object {
        const val KEY_DONE = "done"
        const val KEY_TOTAL = "total"
        const val KEY_CURRENT_ID = "currentId"
        const val KEY_CURRENT_PERCENT = "currentPercent"

        private const val STATUS_EXISTS = "exists"
        private const val STATUS_SAVED = "saved"
        private const val STATUS_UPLOAD = "upload"

        private const val CHUNK_SIZE = 500
        private const val SQL_CHUNK = 500
        private const val MAX_HASH_FAILURES = 3
        private const val MAX_STALLS = 5
        private const val MAX_RESTARTS = 3
        private const val PROGRESS_INTERVAL_MS = 1_000L
        private const val DISCOVERY_TIMEOUT_MS = 10_000L

        private val runLock = Mutex()
    }
}
