package io.github.lgm42.monalbumpc.update

import android.content.BroadcastReceiver
import android.content.Context
import android.content.pm.PackageInstaller
import android.os.Build
import android.os.SystemClock
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.ProcessLifecycleOwner
import io.github.lgm42.monalbumpc.BuildConfig
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.net.ApkInfo
import io.github.lgm42.monalbumpc.net.PcApi
import java.io.File
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/** Where an install started by this process stands. */
enum class InstallPhase { IDLE, INSTALLING, WAITING_FOR_USER, FAILED }

/** Why an install starts: the button, by itself in the foreground, or silently from the background. */
enum class InstallReason { MANUAL, AUTO, SILENT }

/**
 * Self-update from the paired PC: records the APK it offers (`GET /status`), downloads it with
 * [UpdateWorker], installs it with [UpdateInstaller]. Every entry point swallows its errors: the
 * update must never get in the way of a backup.
 */
object UpdateManager {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val installMutex = Mutex()

    private val _downloadProgress = MutableStateFlow<Float?>(null)

    /** Fraction downloaded while [UpdateWorker] runs in this process, null otherwise. */
    val downloadProgress: StateFlow<Float?> = _downloadProgress.asStateFlow()

    private val _downloadFailed = MutableStateFlow(false)
    val downloadFailed: StateFlow<Boolean> = _downloadFailed.asStateFlow()

    private val _installPhase = MutableStateFlow(InstallPhase.IDLE)
    val installPhase: StateFlow<InstallPhase> = _installPhase.asStateFlow()

    private val _dismissedCode = MutableStateFlow(0)

    /** Version hidden by "Plus tard" until the next app start. */
    val dismissedCode: StateFlow<Int> = _dismissedCode.asStateFlow()

    private val _installRequested = MutableStateFlow(false)

    /** Set when the app is opened from the update notification. */
    val installRequested: StateFlow<Boolean> = _installRequested.asStateFlow()

    @Volatile
    private var lastCheckMs = Long.MIN_VALUE / 2

    val installedCode: Int
        get() = BuildConfig.VERSION_CODE

    fun updatesDir(context: Context): File = File(context.applicationContext.filesDir, UPDATES_DIR)

    /** Whether an activity of the app is visible. */
    fun isForeground(): Boolean = try {
        ProcessLifecycleOwner.get().lifecycle.currentState.isAtLeast(Lifecycle.State.STARTED)
    } catch (e: Exception) {
        false
    }

    // region Check

    /** Records the APK offered in a `GET /status` answer and starts its download when needed. */
    suspend fun onStatus(context: Context, apk: ApkInfo?) {
        safely {
            val store = UpdateStore(context)
            if (apk != null && UpdateLogic.isNewer(apk, installedCode)) {
                store.setAvailable(apk)
            } else {
                store.clearAvailable()
            }
            cleanup(context)
            if (UpdateLogic.needsDownload(store.current(), installedCode)) UpdateWorker.enqueue(context)
        }
    }

    /**
     * Lightweight status call, after pairing ([force]) and when the app comes to the foreground
     * (at most every few minutes). Failures are ignored.
     */
    fun checkAsync(context: Context, pc: PairedPc, force: Boolean = false) {
        val appContext = context.applicationContext
        val now = SystemClock.elapsedRealtime()
        if (!force && now - lastCheckMs < CHECK_INTERVAL_MS) return
        lastCheckMs = now
        scope.launch {
            safely {
                val status = withTimeoutOrNull(CHECK_TIMEOUT_MS) {
                    PcApi(pc.host, pc.apiPort, pc.certSha256).status(pc.authorization)
                }
                if (status != null) onStatus(appContext, status.apk)
            }
        }
    }

    // endregion

    // region Files

    /**
     * Keeps only the verified APK of the offered version; forgets an offer the app has reached
     * (after the update, or a manual install).
     */
    suspend fun cleanup(context: Context) {
        val store = UpdateStore(context)
        val state = store.current()
        val installed = installedCode
        if (state.availableCode in 1..installed) store.clearAvailable()
        val path = state.downloadedPath
        val keep = path?.let(::File)?.takeIf {
            state.isDownloaded && !UpdateLogic.isObsolete(state.downloadedCode, installed) && it.isFile
        }
        if (keep == null && (path != null || state.downloadedCode != 0)) store.clearDownloaded()
        if (state.notifiedCode in 1..installed) UpdateNotifications(context).cancel()
        withContext(Dispatchers.IO) {
            // Partial downloads belong to UpdateWorker, which deletes them itself.
            updatesDir(context).listFiles()?.forEach { file ->
                if (file.name.endsWith(APK_SUFFIX) && file != keep) file.delete()
            }
        }
    }

    /** At app start. */
    fun cleanupAsync(context: Context) {
        val appContext = context.applicationContext
        scope.launch { safely { cleanup(appContext) } }
    }

    // endregion

    // region Install

    /** Installs the downloaded update; returns whether a session was committed. */
    suspend fun install(context: Context, reason: InstallReason): Boolean {
        if (!installMutex.tryLock()) return false
        try {
            val store = UpdateStore(context)
            val state = store.current()
            if (!state.isDownloaded || state.availableCode <= installedCode) return false
            val file = File(state.downloadedPath ?: return false)
            if (!file.isFile || file.length() != state.availableSize) {
                cleanup(context)
                return false
            }
            when (reason) {
                // A manual install also counts as the foreground attempt of this version.
                InstallReason.AUTO, InstallReason.MANUAL -> store.setAutoInstalled(state.availableCode)
                InstallReason.SILENT -> store.setSilentInstalled(state.availableCode)
            }
            _installPhase.value = InstallPhase.INSTALLING
            UpdateInstaller.install(context, file, state.availableCode, state.availableName)
            return true
        } catch (e: CancellationException) {
            _installPhase.value = InstallPhase.IDLE
            throw e
        } catch (e: Exception) {
            _installPhase.value = InstallPhase.FAILED
            return false
        } finally {
            installMutex.unlock()
        }
    }

    /**
     * End of a background run, never during a transfer: silent install (Android 12+, once per
     * version), else the "update available" notification (once per version).
     */
    suspend fun afterBackgroundRun(context: Context, backupRunning: Boolean) {
        safely {
            val state = UpdateStore(context).current()
            val action = UpdateLogic.backgroundAction(
                state = state,
                installedCode = installedCode,
                sdkInt = Build.VERSION.SDK_INT,
                inForeground = isForeground(),
                backupRunning = backupRunning,
            )
            when (action) {
                BackgroundAction.SILENT_INSTALL -> install(context, InstallReason.SILENT)
                BackgroundAction.NOTIFY -> notifyOnce(context, state.availableCode, state.availableName)
                BackgroundAction.NONE -> Unit
            }
        }
    }

    private suspend fun notifyOnce(context: Context, versionCode: Int, versionName: String) {
        val store = UpdateStore(context)
        if (!UpdateLogic.shouldNotify(store.current(), versionCode)) return
        if (UpdateNotifications(context).notifyAvailable(versionName)) store.setNotified(versionCode)
    }

    // endregion

    // region Installer results (UpdateResultReceiver)

    fun onInstallWaitingForUser() {
        _installPhase.value = InstallPhase.WAITING_FOR_USER
    }

    fun onInstallNeedsUser(context: Context, versionCode: Int, versionName: String, pending: BroadcastReceiver.PendingResult) {
        _installPhase.value = InstallPhase.IDLE
        val appContext = context.applicationContext
        scope.launch {
            try {
                safely { notifyOnce(appContext, versionCode, versionName) }
            } finally {
                pending.finish()
            }
        }
    }

    fun onInstallFinished() {
        _installPhase.value = InstallPhase.IDLE
    }

    fun onInstallFailed(status: Int) {
        // Cancelled by the user: no error, the card keeps its button.
        _installPhase.value =
            if (status == PackageInstaller.STATUS_FAILURE_ABORTED) InstallPhase.IDLE else InstallPhase.FAILED
    }

    // endregion

    // region UI

    fun dismiss(versionCode: Int) {
        _dismissedCode.value = versionCode
    }

    fun requestInstall() {
        _installRequested.value = true
    }

    fun consumeInstallRequest() {
        _installRequested.value = false
    }

    /** The button when the update is not on disk yet: download it again, even an APK rejected before. */
    suspend fun downloadAgain(context: Context) {
        safely {
            _downloadFailed.value = false
            val store = UpdateStore(context)
            val state = store.current()
            if (state.rejectedSha256 != null) store.clearRejected()
            if (UpdateLogic.needsDownload(store.current(), installedCode)) UpdateWorker.enqueue(context)
        }
    }

    internal fun setDownloadProgress(value: Float?) {
        _downloadProgress.value = value
    }

    internal fun setDownloadFailed(value: Boolean) {
        _downloadFailed.value = value
    }

    // endregion

    private suspend fun safely(block: suspend () -> Unit) {
        try {
            block()
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // Nothing logged: the update is optional and the next check tries again.
        }
    }

    private const val UPDATES_DIR = "updates"
    private const val APK_SUFFIX = ".apk"
    private const val CHECK_INTERVAL_MS = 5 * 60_000L
    private const val CHECK_TIMEOUT_MS = 15_000L
}
