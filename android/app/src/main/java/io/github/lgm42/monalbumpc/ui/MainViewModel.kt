package io.github.lgm42.monalbumpc.ui

import android.app.Application
import android.database.ContentObserver
import android.os.Handler
import android.os.Looper
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.work.WorkInfo
import androidx.work.WorkManager
import io.github.lgm42.monalbumpc.backup.BackupNotifications
import io.github.lgm42.monalbumpc.backup.BackupScheduler
import io.github.lgm42.monalbumpc.backup.BackupWorker
import io.github.lgm42.monalbumpc.backup.BannerLogic
import io.github.lgm42.monalbumpc.backup.BannerState
import io.github.lgm42.monalbumpc.data.BackupPrefs
import io.github.lgm42.monalbumpc.data.BackupState
import io.github.lgm42.monalbumpc.data.media.AppDatabase
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
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/** Progress of the running backup, read from the worker's progress data. */
data class BackupProgress(
    val running: Boolean = false,
    val done: Int = 0,
    val total: Int = 0,
    val currentId: Long? = null,
    val currentPercent: Int = 0,
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

    private val progress: Flow<BackupProgress> =
        WorkManager.getInstance(application).getWorkInfosByTagFlow(BackupScheduler.TAG)
            .map { infos -> progressOf(infos) }
            .catch { emit(BackupProgress()) }

    private val sources: Flow<Sources> = combine(
        media,
        folderStore.tracked,
        dao.observeAll(),
        progress,
        prefs.state,
    ) { mediaList, tracked, index, progress, backup ->
        Sources(mediaList, tracked, index, progress, backup)
    }

    val state: StateFlow<MainUiState> = combine(sources, filter) { sources, filter -> build(sources, filter) }
        .flowOn(Dispatchers.Default)
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5_000), MainUiState())

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
            BackupScheduler.schedule(app, prefs.current().conditions)
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

    fun backupNow() {
        viewModelScope.launch { BackupScheduler.backupNow(app, prefs.current().conditions) }
    }

    fun retry(mediaId: Long) {
        viewModelScope.launch {
            dao.retryErrors(listOf(mediaId))
            BackupScheduler.backupNow(app, prefs.current().conditions)
        }
    }

    fun setChargingOnly(value: Boolean) {
        viewModelScope.launch {
            prefs.setChargingOnly(value)
            BackupScheduler.schedule(app, prefs.current().conditions, replace = true)
        }
    }

    fun setMobileData(value: Boolean) {
        viewModelScope.launch {
            prefs.setMobileData(value)
            BackupScheduler.schedule(app, prefs.current().conditions, replace = true)
        }
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
        // Several runs may be RUNNING while one waits for the other: show the one that reports progress.
        val running = infos.filter { it.state == WorkInfo.State.RUNNING }
            .maxByOrNull { it.progress.getInt(BackupWorker.KEY_TOTAL, 0) } ?: return BackupProgress()
        val data = running.progress
        val currentId = data.getLong(BackupWorker.KEY_CURRENT_ID, -1L)
        return BackupProgress(
            running = true,
            done = data.getInt(BackupWorker.KEY_DONE, 0),
            total = data.getInt(BackupWorker.KEY_TOTAL, 0),
            currentId = currentId.takeIf { it >= 0 },
            currentPercent = data.getInt(BackupWorker.KEY_CURRENT_PERCENT, 0),
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
