package io.github.lgm42.monalbumpc.ui.photos

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.GridItemSpan
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.ErrorOutline
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material3.Button
import androidx.compose.material3.ExtendedFloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Snackbar
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
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
import androidx.compose.ui.draw.alpha
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.components.BackgroundAccessCard
import io.github.lgm42.monalbumpc.ui.components.rememberBackgroundUnrestricted
import io.github.lgm42.monalbumpc.backup.BannerState
import io.github.lgm42.monalbumpc.backup.Elapsed
import io.github.lgm42.monalbumpc.ui.BackupProgress
import io.github.lgm42.monalbumpc.ui.MainUiState
import io.github.lgm42.monalbumpc.ui.UpdateUiState
import io.github.lgm42.monalbumpc.ui.components.UpdateCard
import io.github.lgm42.monalbumpc.ui.components.AppDialog
import io.github.lgm42.monalbumpc.ui.components.MediaThumbnail
import io.github.lgm42.monalbumpc.ui.components.ScreenHeader
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors
import kotlinx.coroutines.launch

private enum class HelpDialog { Unreachable, Destination }

/** Photos tab (design README §2): status banner, filters, month-grouped grid, viewer. */
@Composable
fun PhotosScreen(
    albumName: String?,
    pcName: String,
    state: MainUiState,
    onFilterChange: (GalleryFilter) -> Unit,
    onBackupNow: () -> Unit,
    onRetry: (Long) -> Unit,
    onTrackFolder: (String) -> Unit,
    onOpenFolders: () -> Unit,
    update: UpdateUiState,
    onUpdate: () -> Unit,
    onAllowInstall: () -> Unit,
    onUpdateLater: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    var help by rememberSaveable { mutableStateOf<HelpDialog?>(null) }
    var viewedId by rememberSaveable { mutableStateOf<Long?>(null) }
    val gridState = rememberLazyGridState()

    val unreachableMessage = stringResource(R.string.snackbar_unreachable, pcName)
    val destinationMessage = stringResource(R.string.snackbar_destination)

    val items = remember(state.cells) {
        state.cells.mapNotNull { (it as? GalleryCell.Item)?.item }
    }
    val galleryEmpty = state.loaded && state.items.none { it.state != BadgeState.UNTRACKED }
    val backgroundUnrestricted = rememberBackgroundUnrestricted()

    Box(modifier = modifier.fillMaxSize()) {
        LazyVerticalGrid(
            columns = GridCells.Fixed(4),
            state = gridState,
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 14.dp, end = 14.dp, bottom = 96.dp),
            horizontalArrangement = Arrangement.spacedBy(6.dp),
            verticalArrangement = Arrangement.spacedBy(6.dp),
        ) {
            item(key = "header", span = { GridItemSpan(maxLineSpan) }, contentType = "header") {
                ScreenHeader(
                    title = stringResource(R.string.photos_title),
                    overline = if (albumName.isNullOrBlank()) {
                        stringResource(R.string.photos_greeting)
                    } else {
                        stringResource(R.string.photos_greeting_name, albumName)
                    },
                )
            }
            if (update.showCard) {
                item(key = "update", span = { GridItemSpan(maxLineSpan) }, contentType = "update") {
                    UpdateCard(
                        state = update,
                        onUpdate = onUpdate,
                        onAllowInstall = onAllowInstall,
                        onLater = onUpdateLater,
                        modifier = Modifier.padding(top = 4.dp, bottom = 4.dp),
                    )
                }
            }
            item(key = "banner", span = { GridItemSpan(maxLineSpan) }, contentType = "banner") {
                StatusBanner(
                    banner = state.banner,
                    pcName = pcName,
                    onLink = { banner ->
                        when (banner) {
                            is BannerState.PcUnreachable -> help = HelpDialog.Unreachable
                            BannerState.DestinationUnavailable -> help = HelpDialog.Destination
                            is BannerState.Errors -> onFilterChange(GalleryFilter.ERRORS)
                            else -> Unit
                        }
                    },
                    modifier = Modifier.padding(top = 4.dp, bottom = 8.dp),
                )
            }
            if (!backgroundUnrestricted) {
                item(key = "background", span = { GridItemSpan(maxLineSpan) }, contentType = "background") {
                    BackgroundAccessCard(modifier = Modifier.padding(bottom = 8.dp))
                }
            }
            item(key = "filters", span = { GridItemSpan(maxLineSpan) }, contentType = "filters") {
                FilterRow(
                    selected = state.filter,
                    counts = state.counts,
                    onSelect = onFilterChange,
                    modifier = Modifier.padding(bottom = 4.dp),
                )
            }
            if (state.loaded && state.cells.isEmpty()) {
                item(key = "empty", span = { GridItemSpan(maxLineSpan) }, contentType = "empty") {
                    EmptyState(
                        filter = state.filter,
                        galleryEmpty = galleryEmpty,
                        onOpenFolders = onOpenFolders,
                    )
                }
            }
            items(
                items = state.cells,
                key = { it.key },
                span = { cell -> if (cell is GalleryCell.Header) GridItemSpan(maxLineSpan) else GridItemSpan(1) },
                contentType = { cell -> if (cell is GalleryCell.Header) "month" else "media" },
            ) { cell ->
                when (cell) {
                    is GalleryCell.Header -> MonthHeader(cell)
                    is GalleryCell.Item -> Thumbnail(
                        item = cell.item,
                        progress = state.progress,
                        onClick = { viewedId = cell.item.media.id },
                    )
                }
            }
        }

        val showFab = state.loaded && !state.progress.running && !galleryEmpty
        if (showFab) {
            ExtendedFloatingActionButton(
                onClick = {
                    when (state.banner) {
                        is BannerState.PcUnreachable -> scope.launch { snackbarHostState.showSnackbar(unreachableMessage) }
                        BannerState.DestinationUnavailable ->
                            scope.launch { snackbarHostState.showSnackbar(destinationMessage) }
                        else -> onBackupNow()
                    }
                },
                icon = { Icon(Icons.Outlined.CloudUpload, contentDescription = null) },
                text = { Text(stringResource(R.string.backup_now), style = MaterialTheme.typography.labelLarge) },
                containerColor = MaterialTheme.colorScheme.primary,
                contentColor = MaterialTheme.colorScheme.onPrimary,
                shape = CircleShape,
                modifier = Modifier
                    .align(Alignment.BottomEnd)
                    .padding(16.dp),
            )
        }

        SnackbarHost(
            hostState = snackbarHostState,
            modifier = Modifier
                .align(Alignment.BottomCenter)
                .padding(bottom = 88.dp, start = 16.dp, end = 16.dp),
        ) { data ->
            Snackbar(
                snackbarData = data,
                shape = RoundedCornerShape(14.dp),
                containerColor = MaterialTheme.colorScheme.onBackground,
                contentColor = MaterialTheme.colorScheme.background,
            )
        }
    }

    when (help) {
        HelpDialog.Unreachable -> AppDialog(
            title = stringResource(R.string.help_unreachable_title),
            body = stringResource(R.string.help_unreachable_body),
            confirmLabel = stringResource(R.string.action_ok),
            onConfirm = { help = null },
            onDismiss = { help = null },
        ) {
            Column(modifier = Modifier.padding(top = 8.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf(R.string.error_wifi_check1, R.string.error_wifi_check2, R.string.error_wifi_check3)
                    .forEachIndexed { index, res ->
                        Text(
                            text = "${index + 1}. " + stringResource(res),
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onBackground,
                        )
                    }
            }
        }
        HelpDialog.Destination -> AppDialog(
            title = stringResource(R.string.help_destination_title),
            body = stringResource(R.string.help_destination_body),
            confirmLabel = stringResource(R.string.action_ok),
            onConfirm = { help = null },
            onDismiss = { help = null },
        )
        null -> Unit
    }

    val viewed = viewedId
    if (viewed != null) {
        val index = items.indexOfFirst { it.media.id == viewed }
        if (index >= 0) {
            MediaViewer(
                items = items,
                index = index,
                pcName = pcName,
                progress = state.progress,
                onIndexChange = { viewedId = items[it].media.id },
                onClose = { viewedId = null },
                onRetry = onRetry,
                onTrackFolder = onTrackFolder,
            )
        }
    }
}

@Composable
private fun StatusBanner(
    banner: BannerState,
    pcName: String,
    onLink: (BannerState) -> Unit,
    modifier: Modifier = Modifier,
) {
    val extended = LocalExtendedColors.current
    val warning = banner is BannerState.PcUnreachable || banner == BannerState.DestinationUnavailable ||
        banner == BannerState.DiskFull
    val container = if (warning) extended.warningContainer else MaterialTheme.colorScheme.surface
    val content = if (warning) extended.onWarningContainer else MaterialTheme.colorScheme.onSurface
    val iconTint = if (warning) extended.warningIcon else MaterialTheme.colorScheme.primary

    val (icon: ImageVector, title: String, detail: String?) = when (banner) {
        is BannerState.AllSaved -> Triple(
            Icons.Outlined.CheckCircle,
            stringResource(R.string.status_ok_title),
            banner.lastBackupMs?.let { stringResource(R.string.status_ok_detail, elapsedAgo(it)) },
        )
        is BannerState.Pending -> Triple(
            Icons.Outlined.Schedule,
            pluralStringResource(R.plurals.status_pending_title, banner.count, banner.count),
            stringResource(R.string.status_pending_detail),
        )
        is BannerState.Running -> Triple(
            Icons.Outlined.CloudUpload,
            stringResource(R.string.status_running_title),
            stringResource(R.string.status_running_detail, banner.done, banner.total),
        )
        is BannerState.PcUnreachable -> Triple(
            Icons.Outlined.WarningAmber,
            stringResource(R.string.status_unreachable_title, pcName) +
                (banner.sinceMs?.let { " " + elapsedSince(it) } ?: ""),
            null,
        )
        BannerState.DestinationUnavailable -> Triple(
            Icons.Outlined.WarningAmber,
            stringResource(R.string.status_destination_title),
            stringResource(R.string.status_destination_detail),
        )
        BannerState.DiskFull -> Triple(
            Icons.Outlined.WarningAmber,
            stringResource(R.string.status_disk_full_title),
            stringResource(R.string.status_disk_full_detail),
        )
        is BannerState.Errors -> Triple(
            Icons.Outlined.ErrorOutline,
            pluralStringResource(R.plurals.status_errors_title, banner.count, banner.count),
            stringResource(R.string.status_errors_detail),
        )
        BannerState.Empty -> Triple(
            Icons.Outlined.Image,
            stringResource(R.string.status_empty_title),
            stringResource(R.string.status_empty_detail),
        )
    }
    val link = when (banner) {
        is BannerState.PcUnreachable -> stringResource(R.string.status_unreachable_link)
        BannerState.DestinationUnavailable -> stringResource(R.string.status_destination_link)
        is BannerState.Errors -> stringResource(R.string.status_errors_link)
        else -> null
    }

    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        color = container,
    ) {
        Column {
            Row(
                modifier = Modifier
                    .then(if (link != null) Modifier.clickable { onLink(banner) } else Modifier)
                    .padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Icon(imageVector = icon, contentDescription = null, tint = iconTint, modifier = Modifier.size(24.dp))
                Spacer(Modifier.width(12.dp))
                Text(
                    text = buildAnnotatedString {
                        withStyle(SpanStyle(fontWeight = FontWeight.Bold)) { append(title) }
                        if (detail != null) {
                            append(" · ")
                            append(detail)
                        }
                    },
                    style = MaterialTheme.typography.bodyMedium,
                    color = content,
                    modifier = Modifier.weight(1f),
                )
                if (link != null) {
                    TextButton(onClick = { onLink(banner) }) {
                        Text(
                            text = link,
                            style = MaterialTheme.typography.labelLarge,
                            color = if (warning) content else extended.primaryText,
                        )
                    }
                } else {
                    Spacer(Modifier.width(8.dp))
                }
            }
            if (banner is BannerState.Running) {
                LinearProgressIndicator(
                    progress = { if (banner.total > 0) banner.done.toFloat() / banner.total else 0f },
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(4.dp),
                    color = MaterialTheme.colorScheme.primary,
                    trackColor = MaterialTheme.colorScheme.primaryContainer,
                    gapSize = 0.dp,
                    drawStopIndicator = {},
                )
            }
        }
    }
}

@Composable
private fun FilterRow(
    selected: GalleryFilter,
    counts: Map<GalleryFilter, Int>,
    onSelect: (GalleryFilter) -> Unit,
    modifier: Modifier = Modifier,
) {
    Row(
        modifier = modifier
            .fillMaxWidth()
            .horizontalScroll(rememberScrollState()),
        horizontalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        GalleryFilter.entries.forEach { filter ->
            val isSelected = filter == selected
            FilterChip(
                selected = isSelected,
                onClick = { onSelect(filter) },
                label = {
                    Text(
                        text = stringResource(R.string.filter_with_count, stringResource(filter.labelRes), counts[filter] ?: 0),
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

private val GalleryFilter.labelRes: Int
    get() = when (this) {
        GalleryFilter.ALL -> R.string.filter_all
        GalleryFilter.PENDING -> R.string.filter_pending
        GalleryFilter.ERRORS -> R.string.filter_errors
        GalleryFilter.UNTRACKED -> R.string.filter_untracked
    }

@Composable
private fun MonthHeader(header: GalleryCell.Header) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 14.dp, bottom = 4.dp, start = 2.dp, end = 2.dp),
        verticalAlignment = Alignment.Bottom,
    ) {
        Text(
            text = header.label,
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onBackground,
            modifier = Modifier.weight(1f),
        )
        Text(
            text = pluralStringResource(R.plurals.month_items, header.count, header.count),
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun Thumbnail(item: GalleryItem, progress: BackupProgress, onClick: () -> Unit) {
    val shape = RoundedCornerShape(16.dp)
    Box(
        modifier = Modifier
            .aspectRatio(1f)
            .clip(shape)
            .background(MaterialTheme.colorScheme.surfaceVariant)
            .clickable(onClick = onClick),
    ) {
        MediaThumbnail(
            uri = item.media.uri,
            modifier = Modifier
                .fillMaxSize()
                .alpha(if (item.state == BadgeState.UNTRACKED) 0.35f else 1f),
        )
        if (item.state == BadgeState.UPLOADING) {
            LinearProgressIndicator(
                progress = { progress.currentPercent / 100f },
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .fillMaxWidth()
                    .height(4.dp),
                color = MaterialTheme.colorScheme.primary,
                trackColor = Color.Black.copy(alpha = 0.3f),
                gapSize = 0.dp,
                drawStopIndicator = {},
            )
        }
        if (item.media.isVideo) {
            DurationChip(
                durationMs = item.media.durationMs,
                modifier = Modifier
                    .align(Alignment.BottomStart)
                    .padding(5.dp),
            )
        }
        StateBadge(
            state = item.state,
            modifier = Modifier
                .align(Alignment.BottomEnd)
                .padding(5.dp),
        )
    }
}

@Composable
private fun EmptyState(filter: GalleryFilter, galleryEmpty: Boolean, onOpenFolders: () -> Unit) {
    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 48.dp, horizontal = 16.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        val text = when {
            filter == GalleryFilter.PENDING -> R.string.empty_pending
            filter == GalleryFilter.ERRORS -> R.string.empty_errors
            filter == GalleryFilter.UNTRACKED -> R.string.empty_untracked
            else -> R.string.empty_all
        }
        Text(
            text = stringResource(text),
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.onBackground,
        )
        if (filter == GalleryFilter.ALL && galleryEmpty) {
            Spacer(Modifier.height(16.dp))
            Button(onClick = onOpenFolders, shape = CircleShape, modifier = Modifier.heightIn(min = 48.dp)) {
                Text(stringResource(R.string.empty_choose_folders), style = MaterialTheme.typography.labelLarge)
            }
        }
    }
}

/** "il y a 2 h". */
@Composable
fun elapsedAgo(thenMs: Long): String = when (val e = Elapsed.between(thenMs, System.currentTimeMillis())) {
    Elapsed.JustNow -> stringResource(R.string.elapsed_just_now)
    is Elapsed.Minutes -> pluralStringResource(R.plurals.elapsed_minutes_ago, e.count, e.count)
    is Elapsed.Hours -> pluralStringResource(R.plurals.elapsed_hours_ago, e.count, e.count)
    is Elapsed.Days -> pluralStringResource(R.plurals.elapsed_days_ago, e.count, e.count)
}

/** "depuis 3 jours". */
@Composable
fun elapsedSince(thenMs: Long): String = when (val e = Elapsed.between(thenMs, System.currentTimeMillis())) {
    Elapsed.JustNow -> stringResource(R.string.since_just_now)
    is Elapsed.Minutes -> pluralStringResource(R.plurals.since_minutes, e.count, e.count)
    is Elapsed.Hours -> pluralStringResource(R.plurals.since_hours, e.count, e.count)
    is Elapsed.Days -> pluralStringResource(R.plurals.since_days, e.count, e.count)
}
