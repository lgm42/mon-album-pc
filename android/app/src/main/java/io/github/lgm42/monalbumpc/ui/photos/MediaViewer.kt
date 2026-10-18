package io.github.lgm42.monalbumpc.ui.photos

import android.content.ActivityNotFoundException
import android.content.Intent
import android.net.Uri
import android.text.format.Formatter
import android.widget.Toast
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectHorizontalDragGestures
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ChevronLeft
import androidx.compose.material.icons.filled.ChevronRight
import androidx.compose.material.icons.filled.PlayArrow
import androidx.compose.material3.Button
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import coil.compose.AsyncImage
import coil.request.ImageRequest
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.data.media.ErrorReasons
import io.github.lgm42.monalbumpc.ui.BackupProgress
import io.github.lgm42.monalbumpc.ui.components.MediaThumbnail
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private val dateTimeFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("d MMMM yyyy, HH:mm", Locale.FRENCH)
private val dateFormat: DateTimeFormatter = DateTimeFormatter.ofPattern("dd/MM/yyyy", Locale.FRENCH)

/** Full-screen viewer (black background) with the info sheet (design README §2). */
@Composable
fun MediaViewer(
    items: List<GalleryItem>,
    index: Int,
    pcName: String,
    progress: BackupProgress,
    onIndexChange: (Int) -> Unit,
    onClose: () -> Unit,
    onRetry: (Long) -> Unit,
    onTrackFolder: (String) -> Unit,
) {
    val item = items[index]
    val context = LocalContext.current
    val swipeThreshold = with(LocalDensity.current) { 50.dp.toPx() }
    val currentIndex by rememberUpdatedState(index)
    val currentOnIndexChange by rememberUpdatedState(onIndexChange)

    Dialog(
        onDismissRequest = onClose,
        properties = DialogProperties(usePlatformDefaultWidth = false, decorFitsSystemWindows = false),
    ) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black)
                .pointerInput(items.size) {
                    var total = 0f
                    detectHorizontalDragGestures(
                        onDragStart = { total = 0f },
                        onDragEnd = {
                            if (total <= -swipeThreshold && currentIndex < items.size - 1) currentOnIndexChange(currentIndex + 1)
                            if (total >= swipeThreshold && currentIndex > 0) currentOnIndexChange(currentIndex - 1)
                        },
                        onHorizontalDrag = { _, amount -> total += amount },
                    )
                },
        ) {
            // Media
            Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(bottom = 180.dp),
                contentAlignment = Alignment.Center,
            ) {
                if (item.media.isVideo) {
                    MediaThumbnail(uri = item.media.uri, modifier = Modifier.fillMaxSize(), sizePx = 1080)
                    Box(
                        modifier = Modifier
                            .size(72.dp)
                            .background(Color.Black.copy(alpha = 0.55f), CircleShape)
                            .clickable { playVideo(context, item) },
                        contentAlignment = Alignment.Center,
                    ) {
                        Icon(
                            imageVector = Icons.Filled.PlayArrow,
                            contentDescription = stringResource(R.string.viewer_play),
                            tint = Color.White,
                            modifier = Modifier.size(40.dp),
                        )
                    }
                } else {
                    val request = remember(item.media.uri) {
                        ImageRequest.Builder(context).data(Uri.parse(item.media.uri)).build()
                    }
                    AsyncImage(
                        model = request,
                        contentDescription = null,
                        contentScale = ContentScale.Fit,
                        modifier = Modifier.fillMaxSize(),
                    )
                }
            }

            // Top bar
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(Color.Black.copy(alpha = 0.4f))
                    .statusBarsPadding()
                    .padding(horizontal = 4.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                IconButton(onClick = onClose) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.ArrowBack,
                        contentDescription = stringResource(R.string.action_back),
                        tint = Color.White,
                    )
                }
                val date = dateTimeFormat.format(Instant.ofEpochMilli(item.media.sortDateMs).atZone(ZoneId.systemDefault()))
                Text(
                    text = stringResource(if (item.media.isVideo) R.string.viewer_video else R.string.viewer_photo, date),
                    style = MaterialTheme.typography.bodySmall,
                    color = Color.White,
                    modifier = Modifier.weight(1f),
                )
                Text(
                    text = stringResource(R.string.viewer_position, index + 1, items.size),
                    style = MaterialTheme.typography.bodySmall,
                    color = Color.White,
                    modifier = Modifier.padding(end = 12.dp),
                )
            }

            // Fallback buttons for swiping
            if (index > 0) {
                IconButton(
                    onClick = { onIndexChange(index - 1) },
                    modifier = Modifier
                        .align(Alignment.CenterStart)
                        .padding(4.dp),
                ) {
                    Icon(Icons.Filled.ChevronLeft, stringResource(R.string.viewer_previous), tint = Color.White)
                }
            }
            if (index < items.size - 1) {
                IconButton(
                    onClick = { onIndexChange(index + 1) },
                    modifier = Modifier
                        .align(Alignment.CenterEnd)
                        .padding(4.dp),
                ) {
                    Icon(Icons.Filled.ChevronRight, stringResource(R.string.viewer_next), tint = Color.White)
                }
            }

            InfoSheet(
                item = item,
                pcName = pcName,
                progress = progress,
                onRetry = { onRetry(item.media.id) },
                onTrackFolder = { onTrackFolder(item.media.folderKey) },
                modifier = Modifier.align(Alignment.BottomCenter),
            )
        }
    }
}

@Composable
private fun InfoSheet(
    item: GalleryItem,
    pcName: String,
    progress: BackupProgress,
    onRetry: () -> Unit,
    onTrackFolder: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    Surface(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp),
        color = MaterialTheme.colorScheme.background,
    ) {
        Column(
            modifier = Modifier
                .navigationBarsPadding()
                .padding(20.dp),
        ) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                StateBadge(state = item.state, size = 28.dp)
                Spacer(Modifier.width(12.dp))
                Text(
                    text = stringResource(item.state.labelRes),
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onBackground,
                )
            }
            Spacer(Modifier.height(8.dp))
            Text(
                text = stateSentence(item, pcName, progress),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onBackground,
            )
            Spacer(Modifier.height(12.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                InfoCell(
                    label = stringResource(R.string.viewer_folder),
                    value = item.folderName,
                    modifier = Modifier.weight(1f),
                )
                InfoCell(
                    label = stringResource(R.string.viewer_size),
                    value = Formatter.formatShortFileSize(context, item.media.size),
                    modifier = Modifier.weight(1f),
                )
            }
            val action = when (item.state) {
                BadgeState.ERROR -> stringResource(R.string.retry_now) to onRetry
                BadgeState.UNTRACKED -> stringResource(R.string.track_folder) to onTrackFolder
                else -> null
            }
            if (action != null) {
                Spacer(Modifier.height(16.dp))
                Button(
                    onClick = action.second,
                    shape = CircleShape,
                    modifier = Modifier
                        .fillMaxWidth()
                        .heightIn(min = 52.dp),
                ) {
                    Text(action.first, style = MaterialTheme.typography.labelLarge)
                }
            }
        }
    }
}

@Composable
private fun InfoCell(label: String, value: String, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier
            .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(16.dp))
            .padding(horizontal = 14.dp, vertical = 10.dp),
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            text = value,
            style = MaterialTheme.typography.titleMedium,
            color = MaterialTheme.colorScheme.onSurface,
            maxLines = 2,
        )
    }
}

@Composable
private fun stateSentence(item: GalleryItem, pcName: String, progress: BackupProgress): String = when (item.state) {
    BadgeState.SAVED -> {
        val savedAt = item.savedAtMs
        if (savedAt != null) {
            stringResource(
                R.string.state_saved_body,
                dateFormat.format(Instant.ofEpochMilli(savedAt).atZone(ZoneId.systemDefault())),
                pcName,
            )
        } else {
            stringResource(R.string.state_saved_body_no_date, pcName)
        }
    }
    BadgeState.PENDING -> stringResource(R.string.state_pending_body, pcName)
    BadgeState.UPLOADING -> stringResource(R.string.state_uploading_body, pcName, progress.currentPercent)
    BadgeState.ERROR -> stringResource(
        when (item.errorReason) {
            ErrorReasons.DISK_FULL -> R.string.error_reason_disk_full
            ErrorReasons.DESTINATION_UNAVAILABLE -> R.string.error_reason_destination
            ErrorReasons.HASH_MISMATCH -> R.string.error_reason_hash
            ErrorReasons.UNREADABLE -> R.string.error_reason_unreadable
            else -> R.string.error_reason_server
        },
    )
    BadgeState.UNTRACKED -> stringResource(R.string.state_untracked_body, item.folderName)
}

private fun playVideo(context: android.content.Context, item: GalleryItem) {
    val intent = Intent(Intent.ACTION_VIEW)
        .setDataAndType(Uri.parse(item.media.uri), item.media.mime)
        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    try {
        context.startActivity(intent)
    } catch (e: ActivityNotFoundException) {
        Toast.makeText(context, R.string.no_video_player, Toast.LENGTH_SHORT).show()
    }
}
