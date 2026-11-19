package io.github.lgm42.monalbumpc.ui

import android.app.Application
import android.database.ContentObserver
import android.os.Handler
import android.os.Looper
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkInfo
import androidx.work.WorkManager
import io.github.lgm42.monalbumpc.backup.BackupControl
import io.github.lgm42.monalbumpc.backup.BackupNotifications
import io.github.lgm42.monalbumpc.backup.BackupScheduler
import io.github.lgm42.monalbumpc.backup.BackupWorker
import io.github.lgm42.monalbumpc.backup.BannerLogic
import io.github.lgm42.monalbumpc.backup.BannerState
import io.github.lgm42.monalbumpc.backup.BackupPhase
import io.github.lgm42.monalbumpc.backup.RunActivity
import io.github.lgm42.monalbumpc.backup.BackupOverview
import io.github.lgm42.monalbumpc.backup.BackupSummary
import io.github.lgm42.monalbumpc.backup.DeviceConditions
import io.github.lgm42.monalbumpc.backup.DeviceConditionsMonitor
import io.github.lgm42.monalbumpc.backup.RunTotals
import io.github.lgm42.monalbumpc.data.BackupPrefs
import io.github.lgm42.monalbumpc.data.BackupState
import io.github.lgm42.monalbumpc.data.media.AppDatabase
import io.github.lgm42.monalbumpc.data.media.BackupRunEntity
import io.github.lgm42.monalbumpc.backup.RunHistory
import io.github.lgm42.monalbumpc.data.media.MediaEntity
import io.github.lgm42.monalbumpc.media.DeviceFolder
import io.github.lgm42.monalbumpc.media.DeviceFolders
import io.github.lgm42.monalbumpc.media.DeviceMedia
import io.github.lgm42.monalbumpc.media.FolderStore
import io.github.lgm42.monalbumpc.media.MediaStoreReader
import io.github.lgm42.monalbumpc.ui.photos.BadgeState
import io.github.lgm42.monalbumpc.ui.photos.GalleryCell
import io.github.lgm42.monalbumpc.ui.photos.GalleryFilter
import io.github.lgm42.monalbumpc.ui.photos.GalleryItem
import io.github.lgm42.monalbumpc.ui.photos.GalleryModel
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.shareIn
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/** Progress of the running backup, read from the worker's progress data. */
data class BackupProgress(
    val running: Boolean = false,
    val done: Int = 0,
    val total: Int = 0,
    val currentId: Long? = null,
    val currentPercent: Int = 0,
    /** What the run is doing; null before it needs the PC. */
    val activity: RunActivity? = null,
    /** Files and bytes of the whole run, once its queue is known. */
    val run: RunTotals? = null,
    /** Thumbnail of the file being sent. */
    val currentUri: String? = null,
    /** When WorkManager plans the next automatic run, if any. */
    val nextRunMs: Long? = null,
)

data class MainUiState(
    val loaded: Boolean = false,
    val folders: List<DeviceFolder> = emptyList(),
    val tracked: Set<String> = emptySet(),
    val items: List<GalleryItem> = emptyList(),
    val filter: GalleryFilter = GalleryFilter.ALL,
    val cells: List<GalleryCell> = emptyList(),
    val counts: Map<GalleryFilter, Int> = emptyMap(),
    val banner: BannerState = BannerState.Empty,
    val progress: BackupProgress = BackupProgress(),
    val backup: BackupState = BackupState(),
) {
    val trackedItemCount: Int
        get() = folders.filter { it.key in tracked }.sumOf { it.count }
}

/** State shared by the three tabs: MediaStore, tracked folders, local index and running work. */
class MainViewModel(application: Application) : AndroidViewModel(application) {

    private val app = application
    private val reader = MediaStoreReader(application)
    private val folderStore = FolderStore(application)
    private val prefs = BackupPrefs(application)
    private val dao = AppDatabase.get(application).mediaDao()

    private val media = MutableStateFlow<List<DeviceMedia>?>(null)
    private val filter = MutableStateFlow(GalleryFilter.ALL)

    // Read once for the gallery and the Sauvegarde tab.
    private val index: Flow<List<MediaEntity>> = dao.observeAll()
        .shareIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), replay = 1)

    private val progress: Flow<BackupProgress> =
        WorkManager.getInstance(application).getWorkInfosByTagFlow(BackupScheduler.TAG)
            .map { infos -> progressOf(infos) }
            .catch { emit(BackupProgress()) }

    private val sources: Flow<Sources> = combine(
        media,
        folderStore.tracked,
        index,
        progress,
        prefs.state,
    ) { mediaList, tracked, index, progress, backup ->
        Sources(mediaList, tracked, index, progress, backup)
    }

    val state: StateFlow<MainUiState> = combine(sources, filter) { sources, filter -> build(sources, filter) }
        .flowOn(Dispatchers.Default)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MainUiState())

    /** Counts and lists of the Sauvegarde tab; recomputed when the index changes, not at every progress tick. */
    val summary: StateFlow<BackupSummary?> = combine(media, folderStore.tracked, index) { mediaList, tracked, index ->
        mediaList?.let { BackupOverview.summarize(it, DeviceFolders.fromMedia(it), tracked.orEmpty(), index) }
    }
        .flowOn(Dispatchers.Default)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), null)

    /** The last backup runs, most recent first. */
    val history: StateFlow<List<BackupRunEntity>> = AppDatabase.get(application).backupRunDao().observeRecent(RunHistory.KEEP)
        .catch { emit(emptyList()) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), emptyList())

    /** Network and charging, to tell why the backup waits. */
    val conditions: StateFlow<DeviceConditions> = DeviceConditionsMonitor.observe(application)
        .catch { emit(DeviceConditions()) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), DeviceConditions())

    init {
        BackupNotifications(application).ensureChannels()
        // The first MediaStore read comes from the ON_RESUME refresh of the screen.
        viewModelScope.launch {
            mediaChanges().collectLatest {
                // Debounce bursts of changes (a burst of photos, a copy…).
                delay(REFRESH_DEBOUNCE_MS)
                refresh()
            }
        }
    }

    /** On app start and after each pairing (this view model outlives an unpairing). */
    fun ensureScheduled() {
        viewModelScope.launch {
            // Paused: "Reprendre" schedules the runs again.
            val backup = prefs.current()
            if (!backup.paused) BackupScheduler.schedule(app, backup.conditions)
        }
    }

    fun refresh() {
        viewModelScope.launch {
            val list = reader.readAll()
            if (folderStore.tracked.first() == null) {
                folderStore.trackedOrDefaults(DeviceFolders.fromMedia(list))
            }
            media.value = list
        }
    }

    fun setFilter(value: GalleryFilter) {
        filter.value = value
    }

    fun setFolderTracked(key: String, tracked: Boolean) {
        viewModelScope.launch {
            folderStore.setTracked(key, tracked)
            if (tracked) BackupScheduler.backupNow(app, prefs.current().conditions)
        }
    }

    /** "Sauvegarder maintenant" is an explicit wish to back up: it lifts a pause too. */
    fun backupNow() {
        viewModelScope.launch {
            val backup = prefs.current()
            if (backup.paused) BackupControl.resume(app) else BackupScheduler.backupNow(app, backup.conditions)
        }
    }

    fun pause() {
        viewModelScope.launch { BackupControl.pause(app) }
    }

    fun resume() {
        viewModelScope.launch { BackupControl.resume(app) }
    }

    fun retry(mediaId: Long) {
        viewModelScope.launch {
            dao.retryErrors(listOf(mediaId))
            BackupScheduler.backupNow(app, prefs.current().conditions)
        }
    }

    /** "Réessayer tout": [onDone] gets how many files went back to the queue. */
    fun retryAll(onDone: (Int) -> Unit = {}) {
        viewModelScope.launch {
            val count = dao.retryAllErrors()
            if (count > 0) BackupScheduler.backupNow(app, prefs.current().conditions)
            onDone(count)
        }
    }

    fun setChargingOnly(value: Boolean) {
        viewModelScope.launch {
            prefs.setChargingOnly(value)
            reschedule()
        }
    }

    fun setMobileData(value: Boolean) {
        viewModelScope.launch {
            prefs.setMobileData(value)
            reschedule()
        }
    }

    /** New conditions for the scheduled runs; none while paused ("Reprendre" uses the new ones). */
    private suspend fun reschedule() {
        val backup = prefs.current()
        if (!backup.paused) BackupScheduler.schedule(app, backup.conditions, replace = true)
    }

    private class Sources(
        val media: List<DeviceMedia>?,
        val tracked: Set<String>?,
        val index: List<MediaEntity>,
        val progress: BackupProgress,
        val backup: BackupState,
    )

    private fun build(sources: Sources, filter: GalleryFilter): MainUiState {
        val mediaList = sources.media ?: return MainUiState(filter = filter, backup = sources.backup)
        val tracked = sources.tracked.orEmpty()
        val folders = DeviceFolders.fromMedia(mediaList)
        val folderNames = folders.associate { it.key to it.friendlyName }
        val index = sources.index.associateBy { it.mediaStoreId }
        val uploadingId = sources.progress.currentId.takeIf { sources.progress.running }

        val items = mediaList.map { m ->
            val key = m.folderKey
            val entity = index[m.id]
            GalleryItem(
                media = m,
                state = GalleryModel.badge(key in tracked, entity?.state, uploadingId, m.id),
                folderName = folderNames[key].orEmpty(),
                errorReason = entity?.errorReason,
                savedAtMs = entity?.savedAt,
                errorMessage = entity?.errorMessage,
            )
        }
        val counts = GalleryModel.counts(items)
        val trackedCount = items.count { it.state != BadgeState.UNTRACKED }
        val banner = BannerLogic.compute(
            running = sources.progress.running,
            done = sources.progress.done,
            total = sources.progress.total,
            issue = sources.backup.issue,
            issueSinceMs = sources.backup.issueSinceMs,
            errors = counts[GalleryFilter.ERRORS] ?: 0,
            pending = counts[GalleryFilter.PENDING] ?: 0,
            trackedCount = trackedCount,
            lastBackupMs = sources.backup.lastBackupMs,
            activity = sources.progress.activity,
            issueDetail = sources.backup.issueDetail,
            latestError = BannerLogic.latestError(sources.index),
            checkedMs = sources.backup.lastCheckMs,
            paused = sources.backup.paused,
        )
        return MainUiState(
            loaded = true,
            folders = folders,
            tracked = tracked,
            items = items,
            filter = filter,
            cells = GalleryModel.cells(items.filter { GalleryModel.matches(filter, it.state) }),
            counts = counts,
            banner = banner,
            progress = sources.progress,
            backup = sources.backup,
        )
    }

    private fun progressOf(infos: List<WorkInfo>): BackupProgress {
        // Next automatic run: the closest planned one (the content trigger has no planned time).
        val now = System.currentTimeMillis()
        val nextRunMs = infos.filter { it.state == WorkInfo.State.ENQUEUED }
            .map { it.nextScheduleTimeMillis }
            .filter { it in (now + 1) until Long.MAX_VALUE }
            .minOrNull()
        // Several runs may be RUNNING while one waits for the other: show the one that reports progress.
        val running = infos.filter { it.state == WorkInfo.State.RUNNING }
            .maxWithOrNull(
                compareBy<WorkInfo> { it.progress.getString(BackupWorker.KEY_PHASE) != null }
                    .thenBy { it.progress.getInt(BackupWorker.KEY_TOTAL, 0) },
            ) ?: return BackupProgress(nextRunMs = nextRunMs)
        val data = running.progress
        val currentId = data.getLong(BackupWorker.KEY_CURRENT_ID, -1L)
        val phase = data.getString(BackupWorker.KEY_PHASE)?.let { name -> BackupPhase.entries.firstOrNull { it.name == name } }
        val total = data.getInt(BackupWorker.KEY_TOTAL, 0)
        return BackupProgress(
            running = true,
            done = data.getInt(BackupWorker.KEY_DONE, 0),
            total = total,
            currentId = currentId.takeIf { it >= 0 },
            run = if (total > 0) {
                RunTotals(
                    doneFiles = data.getInt(BackupWorker.KEY_DONE, 0),
                    totalFiles = total,
                    doneBytes = data.getLong(BackupWorker.KEY_RUN_BYTES, 0),
                    totalBytes = data.getLong(BackupWorker.KEY_RUN_SIZE, 0),
                    bytesPerSecond = data.getLong(BackupWorker.KEY_RUN_PACE, 0),
                )
            } else {
                null
            },
            currentUri = data.getString(BackupWorker.KEY_CURRENT_URI),
            currentPercent = data.getInt(BackupWorker.KEY_CURRENT_PERCENT, 0),
            activity = phase?.let {
                RunActivity(
                    phase = it,
                    fileName = data.getString(BackupWorker.KEY_CURRENT_NAME),
                    fileBytes = data.getLong(BackupWorker.KEY_CURRENT_BYTES, 0),
                    fileSize = data.getLong(BackupWorker.KEY_CURRENT_SIZE, 0),
                    bytesPerSecond = data.getLong(BackupWorker.KEY_PACE, 0),
                )
            },
            nextRunMs = nextRunMs,
        )
    }

    /** Emits on every change of the photo and video collections. */
    private fun mediaChanges(): Flow<Unit> = callbackFlow {
        val resolver = app.contentResolver
        val observer = object : ContentObserver(Handler(Looper.getMainLooper())) {
            override fun onChange(selfChange: Boolean) {
                trySend(Unit)
            }
        }
        MediaStoreReader.observedUris.forEach { resolver.registerContentObserver(it, true, observer) }
        awaitClose { resolver.unregisterContentObserver(observer) }
    }

    private companion object {
        const val REFRESH_DEBOUNCE_MS = 1_500L
    }
}
