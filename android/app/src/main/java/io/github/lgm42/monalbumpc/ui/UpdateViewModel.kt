package io.github.lgm42.monalbumpc.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import io.github.lgm42.monalbumpc.BuildConfig
import io.github.lgm42.monalbumpc.backup.BackupWorker
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.update.InstallPhase
import io.github.lgm42.monalbumpc.update.InstallReason
import io.github.lgm42.monalbumpc.update.UpdateLogic
import io.github.lgm42.monalbumpc.update.UpdateManager
import io.github.lgm42.monalbumpc.update.UpdateState
import io.github.lgm42.monalbumpc.update.UpdateStore
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/** What the update card and the "À propos" row show. */
data class UpdateUiState(
    val installedName: String = BuildConfig.VERSION_NAME,
    val availableName: String = "",
    val available: Boolean = false,
    val downloaded: Boolean = false,
    val downloadProgress: Float? = null,
    val downloadFailed: Boolean = false,
    val phase: InstallPhase = InstallPhase.IDLE,
    val canInstallPackages: Boolean = true,
    val dismissed: Boolean = false,
) {
    val showCard: Boolean
        get() = available && !dismissed

    val busy: Boolean
        get() = downloadProgress != null || phase == InstallPhase.INSTALLING || phase == InstallPhase.WAITING_FOR_USER
}

/** Self-update while the app is shown: card, automatic install once per version, "À propos". */
class UpdateViewModel(application: Application) : AndroidViewModel(application) {

    private val app = application
    private val store = UpdateStore(application)
    private val canInstallPackages = MutableStateFlow(canInstall())
    private val resumed = MutableStateFlow(false)

    private val runtime = combine(
        UpdateManager.downloadProgress,
        UpdateManager.downloadFailed,
        UpdateManager.installPhase,
        UpdateManager.dismissedCode,
        canInstallPackages,
    ) { progress, failed, phase, dismissedCode, canInstall ->
        Runtime(progress, failed, phase, dismissedCode, canInstall)
    }

    val state: StateFlow<UpdateUiState> = combine(store.state, runtime) { s, r -> build(s, r) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), UpdateUiState())

    init {
        viewModelScope.launch {
            combine(
                store.state,
                canInstallPackages,
                resumed,
                BackupWorker.running,
                UpdateManager.installRequested,
            ) { s, canInstall, isResumed, backupRunning, requested ->
                Trigger(s, canInstall, isResumed, backupRunning, requested)
            }.collect { t -> onTrigger(t) }
        }
    }

    /** Each time the app comes to the foreground while paired. */
    fun onResume(pc: PairedPc) {
        canInstallPackages.value = canInstall()
        resumed.value = true
        UpdateManager.checkAsync(app, pc)
    }

    fun onPause() {
        resumed.value = false
    }

    /** "Mettre à jour": install, or download again when the file is not there yet. */
    fun update() {
        viewModelScope.launch {
            if (store.current().isDownloaded) {
                UpdateManager.install(app, InstallReason.MANUAL)
            } else {
                UpdateManager.downloadAgain(app)
            }
        }
    }

    /** "Plus tard": hidden until the next app start. */
    fun later() {
        viewModelScope.launch { UpdateManager.dismiss(store.current().availableCode) }
    }

    private suspend fun onTrigger(t: Trigger) {
        if (!t.resumed) return
        val installed = UpdateManager.installedCode
        if (t.requested) {
            // Opened from the notification.
            UpdateManager.consumeInstallRequest()
            if (t.state.isDownloaded) {
                UpdateManager.install(app, InstallReason.MANUAL)
            } else {
                UpdateManager.downloadAgain(app)
            }
            return
        }
        if (UpdateLogic.shouldAutoInstall(t.state, installed, t.canInstall, t.backupRunning)) {
            UpdateManager.install(app, InstallReason.AUTO)
        }
    }

    private fun build(s: UpdateState, r: Runtime): UpdateUiState {
        val available = s.availableCode > UpdateManager.installedCode
        return UpdateUiState(
            availableName = s.availableName,
            available = available,
            downloaded = s.isDownloaded,
            downloadProgress = r.progress,
            downloadFailed = r.downloadFailed,
            phase = r.phase,
            canInstallPackages = r.canInstall,
            dismissed = available && r.dismissedCode == s.availableCode,
        )
    }

    private fun canInstall(): Boolean = try {
        app.packageManager.canRequestPackageInstalls()
    } catch (e: Exception) {
        true
    }

    private data class Runtime(
        val progress: Float?,
        val downloadFailed: Boolean,
        val phase: InstallPhase,
        val dismissedCode: Int,
        val canInstall: Boolean,
    )

    private data class Trigger(
        val state: UpdateState,
        val canInstall: Boolean,
        val resumed: Boolean,
        val backupRunning: Boolean,
        val requested: Boolean,
    )
}
