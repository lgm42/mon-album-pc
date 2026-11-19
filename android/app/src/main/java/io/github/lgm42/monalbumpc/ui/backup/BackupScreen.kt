package io.github.lgm42.monalbumpc.ui.backup

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.SnackbarResult
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.backup.BackupHeadline
import io.github.lgm42.monalbumpc.backup.BackupIssue
import io.github.lgm42.monalbumpc.backup.BackupOverview
import io.github.lgm42.monalbumpc.backup.BackupPhase
import io.github.lgm42.monalbumpc.backup.BackupSummary
import io.github.lgm42.monalbumpc.backup.DeviceConditions
import io.github.lgm42.monalbumpc.backup.FailedEntry
import io.github.lgm42.monalbumpc.backup.FolderProgress
import io.github.lgm42.monalbumpc.backup.QueuedFile
import io.github.lgm42.monalbumpc.backup.RunActivity
import io.github.lgm42.monalbumpc.backup.RunTotals
import io.github.lgm42.monalbumpc.backup.SavedEntry
import io.github.lgm42.monalbumpc.backup.Tally
import io.github.lgm42.monalbumpc.backup.TransferText
import io.github.lgm42.monalbumpc.backup.UntrackedFolder
import io.github.lgm42.monalbumpc.data.BackupState
import io.github.lgm42.monalbumpc.data.media.BackupRunEntity
import io.github.lgm42.monalbumpc.ui.BackupProgress
import io.github.lgm42.monalbumpc.ui.components.BackgroundAccessCard
import io.github.lgm42.monalbumpc.ui.components.MediaThumbnail
import io.github.lgm42.monalbumpc.ui.components.ScreenHeader
import io.github.lgm42.monalbumpc.ui.components.rememberBackgroundUnrestricted
import io.github.lgm42.monalbumpc.ui.folders.formatCount
import io.github.lgm42.monalbumpc.ui.photos.DurationChip
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme
import kotlinx.coroutines.launch

/** Lists at the bottom of the Sauvegarde tab. */
enum class BackupList { QUEUE, FAILURES, RECENT, HISTORY }

/**
 * Sauvegarde tab: what the backup does right now (with the file being sent and the whole run), what is saved, waiting
 * or failed, per folder, the queue, the failures with their retry, the last saved files; and the actions.
 */
@Composable
fun BackupScreen(
    pcName: String,
    progress: BackupProgress,
    backup: BackupState,
    summary: BackupSummary?,
    history: List<BackupRunEntity>,
    conditions: DeviceConditions,
    onBackupNow: () -> Unit,
    onRetry: (Long) -> Unit,
    onRetryAll: (onDone: (Int) -> Unit) -> Unit,
    onOpenFolders: () -> Unit,
    onPause: () -> Unit,
    onResume: () -> Unit,
    modifier: Modifier = Modifier,
    backgroundUnrestricted: Boolean = rememberBackgroundUnrestricted(),
) {
    val context = LocalContext.current
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    val listState = rememberLazyListState()
    var selected by rememberSaveable { mutableStateOf(BackupList.QUEUE) }
    val now = rememberNow()

    val headline = summary?.let {
        BackupOverview.headline(
            summary = it,
            running = progress.running,
            done = progress.done,
            total = progress.total,
            activity = progress.activity,
            run = progress.run,
            paused = backup.paused,
            conditions = conditions,
            chargingOnly = backup.chargingOnly,
            mobileData = backup.mobileData,
            issue = backup.issue,
            issueSinceMs = backup.issueSinceMs,
            issueDetail = backup.issueDetail,
            checkedMs = backup.lastCheckMs ?: backup.lastBackupMs,
            nextRunMs = progress.nextRunMs,
        )
    }
    val data = summary ?: BackupSummary()

    fun say(message: String, actionLabel: String? = null, onAction: () -> Unit = {}) {
        scope.launch {
            snackbarHostState.currentSnackbarData?.dismiss()
            val result = snackbarHostState.showSnackbar(message, actionLabel)
            if (result == SnackbarResult.ActionPerformed) onAction()
        }
    }

    val startedMessage = stringResource(R.string.snackbar_backup_started, pcName)
    val wifiMessage = stringResource(R.string.snackbar_backup_wifi)
    val networkMessage = stringResource(R.string.snackbar_backup_network)
    val destinationMessage = stringResource(R.string.snackbar_destination)
    val noFailureMessage = stringResource(R.string.snackbar_retry_none)
    val pausedMessage = stringResource(R.string.snackbar_paused)
    val resumedMessage = stringResource(R.string.snackbar_resumed, pcName)
    val resumeLabel = stringResource(R.string.backup_resume)
    val resume = {
        onResume()
        say(resumedMessage)
    }
    val pause = {
        onPause()
        say(pausedMessage, resumeLabel, resume)
    }
    val backupNow = {
        onBackupNow()
        say(
            when {
                !conditions.networkOk(backup.mobileData) -> if (backup.mobileData) networkMessage else wifiMessage
                headline == BackupHeadline.DestinationUnavailable -> destinationMessage
                else -> startedMessage
            },
        )
    }
    val retryAll = {
        onRetryAll { count ->
            say(
                if (count > 0) {
                    context.resources.getQuantityString(R.plurals.snackbar_retry_all, count, count)
                } else {
                    noFailureMessage
                },
            )
        }
    }
    val retryOne = { entry: FailedEntry ->
        onRetry(entry.id)
        say(context.getString(R.string.snackbar_retry_one, entry.name))
    }

    val actions = buildList {
        when (headline) {
            null -> Unit
            BackupHeadline.Paused -> add(StatusAction(resumeLabel, onClick = resume))
            is BackupHeadline.Running -> add(StatusAction(stringResource(R.string.backup_pause), primary = false, onClick = pause))
            else -> {
                add(StatusAction(stringResource(R.string.backup_now), onClick = backupNow))
                if (data.failed.count > 0) add(StatusAction(stringResource(R.string.backup_retry_failures), primary = false, onClick = retryAll))
                add(StatusAction(stringResource(R.string.backup_pause), primary = false, onClick = pause))
            }
        }
    }

    // Index of the list chips, to scroll there from the counters.
    val folderItems = if (summary == null) 0 else data.folders.size.coerceAtLeast(1)
    val chipsIndex = 3 + (if (backgroundUnrestricted) 0 else 1) + 1 + folderItems +
        (if (data.untracked.isEmpty()) 0 else 1)
    val showList: (BackupList) -> Unit = { list ->
        selected = list
        scope.launch { listState.animateScrollToItem(chipsIndex) }
    }

    Box(modifier = modifier.fillMaxSize()) {
        LazyColumn(
            state = listState,
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(bottom = 24.dp),
        ) {
            item(key = "header", contentType = "header") {
                ScreenHeader(title = stringResource(R.string.backup_title))
            }
            item(key = "status", contentType = "status") {
                BackupStatusCard(
                    headline = headline,
                    pcName = pcName,
                    lastContactMs = backup.lastContactMs,
                    nowMs = now,
                    currentUri = progress.currentUri,
                    actions = actions,
                    modifier = Modifier.padding(horizontal = 16.dp),
                )
            }
            if (!backgroundUnrestricted) {
                item(key = "background", contentType = "background") {
                    BackgroundAccessCard(modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp))
                }
            }
            item(key = "counters", contentType = "counters") {
                Counters(
                    summary = data,
                    onSelect = showList,
                    modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 12.dp),
                )
            }
            item(key = "folders-title", contentType = "section") {
                SectionTitle(stringResource(R.string.backup_folders_title))
            }
            // Before MediaStore is read, nothing is known: no "no folder" flash.
            if (summary != null && data.folders.isEmpty()) {
                item(key = "folders-none", contentType = "note") {
                    Note(stringResource(R.string.backup_folders_none))
                }
            }
            items(data.folders, key = { "folder-${it.key}" }, contentType = { "folder" }) { folder ->
                FolderRow(folder, Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
            }
            if (data.untracked.isNotEmpty()) {
                item(key = "untracked", contentType = "untracked") {
                    UntrackedRow(
                        folders = data.untracked,
                        onOpenFolders = onOpenFolders,
                        modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp),
                    )
                }
            }
            item(key = "lists", contentType = "chips") {
                ListChips(
                    selected = selected,
                    summary = data,
                    onSelect = { selected = it },
                    modifier = Modifier.padding(start = 16.dp, end = 16.dp, top = 20.dp, bottom = 8.dp),
                )
            }
            when (selected) {
                BackupList.QUEUE -> queueItems(data.queue, progress)
                BackupList.FAILURES -> failureItems(data.failures, pcName, now, retryOne, retryAll)
                BackupList.RECENT -> recentItems(data.recent)
                BackupList.HISTORY -> historyItems(history, pcName)
            }
        }

        SnackbarHost(
            hostState = snackbarHostState,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 16.dp, start = 16.dp, end = 16.dp),
        ) { snackbar ->
            Snackbar(
                snackbarData = snackbar,
                shape = RoundedCornerShape(14.dp),
                containerColor = MaterialTheme.colorScheme.onBackground,
                contentColor = MaterialTheme.colorScheme.background,
                actionColor = MaterialTheme.colorScheme.inversePrimary,
            )
        }
    }
}

@Composable
private fun SectionTitle(text: String, modifier: Modifier = Modifier) {
    Text(
        text = text,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.onBackground,
        modifier = modifier.padding(start = 20.dp, end = 20.dp, top = 20.dp, bottom = 8.dp),
    )
}

@Composable
private fun Note(text: String) {
    Text(
        text = text,
        style = MaterialTheme.typography.bodyMedium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(horizontal = 20.dp, vertical = 12.dp),
    )
}

/** Saved / waiting / failed, with their sizes; each opens its list. */
@Composable
private fun Counters(summary: BackupSummary, onSelect: (BackupList) -> Unit, modifier: Modifier = Modifier) {
    Row(modifier = modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
        CounterTile(stringResource(R.string.backup_count_saved), summary.saved, { onSelect(BackupList.RECENT) }, Modifier.weight(1f))
        CounterTile(stringResource(R.string.backup_count_pending), summary.pending, { onSelect(BackupList.QUEUE) }, Modifier.weight(1f))
        CounterTile(
            label = stringResource(R.string.backup_count_failed),
            tally = summary.failed,
            onClick = { onSelect(BackupList.FAILURES) },
            modifier = Modifier.weight(1f),
            alert = summary.failed.count > 0,
        )
    }
}

@Composable
private fun CounterTile(label: String, tally: Tally, onClick: () -> Unit, modifier: Modifier = Modifier, alert: Boolean = false) {
    val context = LocalContext.current
    Surface(
        modifier = modifier
            .clip(RoundedCornerShape(18.dp))
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(18.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Column(modifier = Modifier.padding(horizontal = 12.dp, vertical = 12.dp)) {
            Text(
                text = formatCount(tally.count),
                style = MaterialTheme.typography.headlineSmall,
                color = if (alert) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
            )
            Text(text = label, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurface)
            Text(
                text = TransferText.size(context, tally.bytes),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

/** "Appareil photo 1 608 / 1 610" with a small bar. */
@Composable
private fun FolderRow(folder: FolderProgress, modifier: Modifier = Modifier) {
    Surface(modifier = modifier.fillMaxWidth(), shape = RoundedCornerShape(22.dp), color = MaterialTheme.colorScheme.surface) {
        Row(
            modifier = Modifier
                .heightIn(min = 64.dp)
                .padding(horizontal = 12.dp, vertical = 10.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(44.dp)
                    .clip(RoundedCornerShape(14.dp))
                    .background(MaterialTheme.colorScheme.surfaceVariant),
            ) {
                folder.newestUri?.let { MediaThumbnail(uri = it, modifier = Modifier.fillMaxSize(), sizePx = 128) }
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = folder.name,
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    Spacer(Modifier.width(8.dp))
                    Text(
                        text = stringResource(R.string.backup_folder_count, formatCount(folder.saved), formatCount(folder.total)),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                SmoothBar(
                    fraction = folder.fraction,
                    resetKey = folder.key,
                    height = 4.dp,
                    modifier = Modifier.padding(top = 6.dp),
                    trackColor = MaterialTheme.colorScheme.background,
                )
                if (folder.failed > 0) {
                    Text(
                        text = pluralStringResource(R.plurals.backup_folder_failed, folder.failed, folder.failed),
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.error,
                        modifier = Modifier.padding(top = 2.dp),
                    )
                }
            }
        }
    }
}

/** "3 dossiers non suivis : Snapchat, Téléchargements…" and a link to the Dossiers tab. */
@Composable
private fun UntrackedRow(folders: List<UntrackedFolder>, onOpenFolders: () -> Unit, modifier: Modifier = Modifier) {
    val names = folders.take(UNTRACKED_NAMES).joinToString(", ") { it.name } + if (folders.size > UNTRACKED_NAMES) "…" else ""
    Surface(
        modifier = modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(22.dp))
            .clickable(onClick = onOpenFolders),
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Column(modifier = Modifier.padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 4.dp)) {
            Text(
                text = pluralStringResource(R.plurals.backup_untracked, folders.size, folders.size, names),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(end = 8.dp),
            )
            TextButton(onClick = onOpenFolders) {
                Text(
                    text = stringResource(R.string.backup_untracked_link),
                    style = MaterialTheme.typography.labelLarge,
                    color = LocalExtendedColors.current.primaryText,
                )
            }
        }
    }
}

@Composable
private fun ListChips(selected: BackupList, summary: BackupSummary, onSelect: (BackupList) -> Unit, modifier: Modifier = Modifier) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        BackupList.entries.forEach { list ->
            val isSelected = list == selected
            val label = when (list) {
                BackupList.QUEUE -> stringResource(R.string.filter_with_count, stringResource(R.string.backup_list_queue), summary.pending.count)
                BackupList.FAILURES -> stringResource(R.string.filter_with_count, stringResource(R.string.backup_list_failures), summary.failed.count)
                BackupList.RECENT -> stringResource(R.string.backup_list_recent)
                BackupList.HISTORY -> stringResource(R.string.backup_list_history)
            }
            FilterChip(
                selected = isSelected,
                onClick = { onSelect(list) },
                label = {
                    Text(
                        text = label,
                        style = MaterialTheme.typography.labelMedium,
                        fontWeight = if (isSelected) FontWeight.Bold else FontWeight.Normal,
                    )
                },
                shape = RoundedCornerShape(24.dp),
                colors = FilterChipDefaults.filterChipColors(
                    containerColor = MaterialTheme.colorScheme.surface,
                    labelColor = MaterialTheme.colorScheme.onSurface,
                    selectedContainerColor = MaterialTheme.colorScheme.primaryContainer,
                    selectedLabelColor = MaterialTheme.colorScheme.onPrimaryContainer,
                ),
                border = null,
                modifier = Modifier.heightIn(min = 48.dp),
            )
        }
    }
}

private fun LazyListScope.queueItems(queue: List<QueuedFile>, progress: BackupProgress) {
    if (queue.isEmpty()) {
        item(key = "queue-empty", contentType = "note") { Note(stringResource(R.string.backup_empty_queue)) }
        return
    }
    val sendingId = progress.currentId.takeIf { progress.running }
    // The file being sent first, then the queue in the order of the backup.
    val ordered = if (sendingId != null && queue.any { it.id == sendingId }) {
        queue.filter { it.id == sendingId } + queue.filter { it.id != sendingId }
    } else {
        queue
    }
    items(ordered, key = { "queue-${it.id}" }, contentType = { "file" }) { file ->
        val context = LocalContext.current
        val sending = file.id == sendingId
        FileRow(
            uri = file.uri,
            durationMs = file.durationMs.takeIf { file.isVideo },
            name = file.name,
            line = if (sending) {
                listOfNotNull(
                    stringResource(R.string.backup_queue_sending),
                    progress.activity?.let { TransferText.fileBytes(context, it) },
                ).joinToString(" · ")
            } else {
                stringResource(R.string.backup_queue_line, TransferText.size(context, file.size), file.folderName)
            },
            highlighted = sending,
        )
    }
}

private fun LazyListScope.failureItems(
    failures: List<FailedEntry>,
    pcName: String,
    nowMs: Long,
    onRetry: (FailedEntry) -> Unit,
    onRetryAll: () -> Unit,
) {
    if (failures.isEmpty()) {
        item(key = "failures-empty", contentType = "note") { Note(stringResource(R.string.backup_empty_failures)) }
        return
    }
    item(key = "failures-all", contentType = "retry-all") {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(start = 20.dp, end = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = pluralStringResource(R.plurals.status_errors_title, failures.size, failures.size),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onBackground,
                modifier = Modifier.weight(1f),
            )
            TextButton(onClick = onRetryAll) {
                Text(
                    text = stringResource(R.string.backup_retry_all),
                    style = MaterialTheme.typography.labelLarge,
                    color = LocalExtendedColors.current.primaryText,
                )
            }
        }
    }
    items(failures, key = { "failure-${it.id}" }, contentType = { "failure" }) { entry ->
        FileRow(
            uri = entry.uri,
            durationMs = entry.durationMs.takeIf { entry.isVideo },
            name = entry.name,
            line = failureReason(entry, pcName),
            secondLine = nextAttempt(entry.retryAfterMs, nowMs),
            action = StatusAction(stringResource(R.string.backup_retry), onClick = { onRetry(entry) }),
        )
    }
}

private fun LazyListScope.recentItems(recent: List<SavedEntry>) {
    if (recent.isEmpty()) {
        item(key = "recent-empty", contentType = "note") { Note(stringResource(R.string.backup_empty_recent)) }
        return
    }
    items(recent, key = { "recent-${it.id}" }, contentType = { "file" }) { entry ->
        val context = LocalContext.current
        FileRow(
            uri = entry.uri,
            durationMs = entry.durationMs.takeIf { entry.isVideo },
            name = entry.name,
            line = stringResource(R.string.backup_saved_line, TransferText.size(context, entry.size), moment(entry.savedAtMs)),
        )
    }
}

/** A file of a list: thumbnail, name, one or two lines, an optional action. */
@Composable
private fun FileRow(
    uri: String,
    durationMs: Long?,
    name: String,
    line: String,
    secondLine: String? = null,
    highlighted: Boolean = false,
    action: StatusAction? = null,
) {
    Surface(
        modifier = Modifier
            .padding(horizontal = 16.dp, vertical = 4.dp)
            .fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        color = if (highlighted) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
    ) {
        Row(
            modifier = Modifier
                .heightIn(min = 64.dp)
                .padding(start = 10.dp, end = 8.dp, top = 8.dp, bottom = 8.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Box(
                modifier = Modifier
                    .size(48.dp)
                    .clip(RoundedCornerShape(12.dp))
                    .background(MaterialTheme.colorScheme.surfaceVariant),
            ) {
                MediaThumbnail(uri = uri, modifier = Modifier.fillMaxSize(), sizePx = 128)
                if (durationMs != null) {
                    DurationChip(durationMs = durationMs, modifier = Modifier.align(Alignment.BottomStart).padding(2.dp))
                }
            }
            Spacer(Modifier.width(12.dp))
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = name,
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(text = line, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                if (secondLine != null) {
                    Text(text = secondLine, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
            if (action != null) {
                TextButton(onClick = action.onClick) {
                    Text(
                        text = action.label,
                        style = MaterialTheme.typography.labelLarge,
                        color = LocalExtendedColors.current.primaryText,
                    )
                }
            }
        }
    }
}

private const val UNTRACKED_NAMES = 4

private val previewSummary = BackupSummary(
    saved = Tally(1_608, 12_300_000_000),
    pending = Tally(52, 340_000_000),
    failed = Tally(1, 45_000_000),
    folders = listOf(
        FolderProgress("a", "Appareil photo", 1_560, 1_610, 1, null),
        FolderProgress("b", "WhatsApp Images", 48, 50, 0, null),
    ),
    untracked = listOf(UntrackedFolder("c", "Snapchat", 120), UntrackedFolder("d", "Téléchargements", 45)),
    queue = listOf(QueuedFile(1, "VID_20261119_181500.mp4", "", 120_000_000, true, "Appareil photo", 0)),
    failures = listOf(FailedEntry(2, "IMG_0042.jpg", "", 3_000_000, false, "server", "Rangement impossible", null, 1)),
)

@Preview(showBackground = true, heightDp = 1200)
@Composable
private fun BackupScreenRunningPreview() {
    MonAlbumPcTheme {
        BackupScreen(
            pcName = "NAS",
            progress = BackupProgress(
                running = true,
                done = 3,
                total = 20,
                currentId = 1,
                activity = RunActivity(BackupPhase.UPLOADING, "VID_20261119_181500.mp4", 45_000_000, 120_000_000, 2_100_000),
                run = RunTotals(3, 20, 200_000_000, 900_000_000, 3_000_000),
            ),
            backup = BackupState(lastContactMs = System.currentTimeMillis()),
            summary = previewSummary,
            history = emptyList(),
            conditions = DeviceConditions(),
            onBackupNow = {},
            onRetry = {},
            onRetryAll = {},
            onOpenFolders = {},
            onPause = {},
            onResume = {},
            backgroundUnrestricted = true,
        )
    }
}

@Preview(showBackground = true, heightDp = 900)
@Composable
private fun BackupScreenUnreachablePreview() {
    MonAlbumPcTheme {
        BackupScreen(
            pcName = "NAS",
            progress = BackupProgress(),
            backup = BackupState(
                issue = BackupIssue.PC_UNREACHABLE,
                issueSinceMs = System.currentTimeMillis() - 12 * 60_000,
                issueDetail = "nas.local:48620",
            ),
            summary = previewSummary,
            history = emptyList(),
            conditions = DeviceConditions(),
            onBackupNow = {},
            onRetry = {},
            onRetryAll = {},
            onOpenFolders = {},
            onPause = {},
            onResume = {},
            backgroundUnrestricted = true,
        )
    }
}
