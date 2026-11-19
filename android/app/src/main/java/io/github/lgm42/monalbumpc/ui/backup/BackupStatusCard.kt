package io.github.lgm42.monalbumpc.ui.backup

import androidx.compose.animation.animateContentSize
import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.BatteryChargingFull
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.CloudUpload
import androidx.compose.material.icons.outlined.ErrorOutline
import androidx.compose.material.icons.outlined.Image
import androidx.compose.material.icons.outlined.PauseCircle
import androidx.compose.material.icons.outlined.Schedule
import androidx.compose.material.icons.outlined.WarningAmber
import androidx.compose.material.icons.outlined.WifiOff
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.backup.BackupHeadline
import io.github.lgm42.monalbumpc.backup.RunActivity
import io.github.lgm42.monalbumpc.backup.RunTotals
import io.github.lgm42.monalbumpc.backup.TransferText
import io.github.lgm42.monalbumpc.ui.components.MediaThumbnail
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors

/** A button of the status card or a list row; [primary] is the filled one. */
data class StatusAction(val label: String, val primary: Boolean = true, val onClick: () -> Unit)

/**
 * Big state of the backup (what runs, what blocks, what waits), the live run with its two bars (the file being sent,
 * the whole run), the server and when it last answered, and the actions that fit the state.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
fun BackupStatusCard(
    headline: BackupHeadline?,
    pcName: String,
    lastContactMs: Long?,
    nowMs: Long,
    currentUri: String?,
    actions: List<StatusAction>,
    modifier: Modifier = Modifier,
) {
    val extended = LocalExtendedColors.current
    val warning = headline is BackupHeadline.Unreachable || headline == BackupHeadline.DestinationUnavailable ||
        headline == BackupHeadline.DiskFull
    val container = if (warning) extended.warningContainer else MaterialTheme.colorScheme.surface
    val content = if (warning) extended.onWarningContainer else MaterialTheme.colorScheme.onSurface
    val badge = if (warning) extended.warningContainer else MaterialTheme.colorScheme.primaryContainer
    val iconTint = if (warning) extended.warningIcon else MaterialTheme.colorScheme.onPrimaryContainer

    val (icon, title, detail) = headlineTexts(headline, pcName, nowMs)

    Surface(
        modifier = modifier
            .fillMaxWidth()
            .animateContentSize(),
        shape = RoundedCornerShape(24.dp),
        color = container,
    ) {
        Column(modifier = Modifier.padding(start = 18.dp, end = 18.dp, top = 18.dp, bottom = 16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(
                    modifier = Modifier
                        .size(48.dp)
                        .background(if (warning) extended.warningIcon.copy(alpha = 0.18f) else badge, CircleShape),
                    contentAlignment = Alignment.Center,
                ) {
                    Icon(imageVector = icon, contentDescription = null, tint = iconTint)
                }
                Spacer(Modifier.width(14.dp))
                Column(modifier = Modifier.weight(1f)) {
                    Text(text = title, style = MaterialTheme.typography.titleLarge, color = content)
                    if (detail != null) {
                        Text(
                            text = detail,
                            style = MaterialTheme.typography.bodyMedium,
                            color = content.copy(alpha = 0.8f),
                        )
                    }
                }
            }

            if (headline is BackupHeadline.Running) {
                LiveRun(headline, pcName, currentUri, Modifier.padding(top = 14.dp))
            }

            Text(
                text = if (lastContactMs != null) {
                    stringResource(R.string.backup_server_contact, pcName, moment(lastContactMs))
                } else {
                    stringResource(R.string.backup_server_never, pcName)
                },
                style = MaterialTheme.typography.bodySmall,
                color = content.copy(alpha = 0.7f),
                modifier = Modifier.padding(top = 12.dp),
            )

            if (actions.isNotEmpty()) {
                FlowRow(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 12.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    actions.forEach { action ->
                        Button(
                            onClick = action.onClick,
                            shape = CircleShape,
                            modifier = Modifier.heightIn(min = 48.dp),
                            colors = if (action.primary) {
                                ButtonDefaults.buttonColors()
                            } else {
                                ButtonDefaults.buttonColors(
                                    containerColor = MaterialTheme.colorScheme.background,
                                    contentColor = MaterialTheme.colorScheme.onBackground,
                                )
                            },
                        ) {
                            Text(action.label, style = MaterialTheme.typography.labelLarge)
                        }
                    }
                }
            }
        }
    }
}

/** The step, then the file being sent (thumbnail, name, bar, bytes and pace), then the whole run. */
@Composable
private fun LiveRun(running: BackupHeadline.Running, pcName: String, currentUri: String?, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val activity = running.activity
    Column(modifier = modifier.fillMaxWidth()) {
        Text(
            text = if (activity != null) {
                TransferText.title(context, running.done, running.total, activity, pcName)
            } else {
                stringResource(R.string.backup_reading)
            },
            style = MaterialTheme.typography.titleSmall,
            color = MaterialTheme.colorScheme.onSurface,
        )
        val fileName = activity?.fileName
        if (activity != null && fileName != null) {
            CurrentFile(activity, fileName, currentUri, Modifier.padding(top = 10.dp))
        }
        val run = running.run
        if (run != null) {
            RunLevel(run, Modifier.padding(top = 14.dp))
        }
    }
}

@Composable
private fun CurrentFile(activity: RunActivity, fileName: String, currentUri: String?, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    // Keeps the last thumbnail between two files rather than flashing an empty square.
    var shownUri by remember { mutableStateOf(currentUri) }
    if (currentUri != null && currentUri != shownUri) shownUri = currentUri
    Row(modifier = modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Box(
            modifier = Modifier
                .size(56.dp)
                .clip(RoundedCornerShape(14.dp))
                .background(MaterialTheme.colorScheme.surfaceVariant),
        ) {
            shownUri?.let { MediaThumbnail(uri = it, modifier = Modifier.fillMaxSize(), sizePx = 160) }
        }
        Spacer(Modifier.width(12.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = stringResource(R.string.backup_level_file),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            Text(
                text = fileName,
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            val fraction = activity.fileFraction
            if (fraction != null) {
                SmoothBar(fraction = fraction, resetKey = fileName, height = 6.dp, modifier = Modifier.padding(top = 6.dp))
                Text(
                    text = TransferText.bytes(context, activity).orEmpty(),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
        }
    }
}

@Composable
private fun RunLevel(run: RunTotals, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    Column(modifier = modifier.fillMaxWidth()) {
        Row(verticalAlignment = Alignment.Bottom) {
            Text(
                text = stringResource(R.string.backup_level_run),
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.weight(1f),
            )
            TransferText.runLeft(context, run)?.let { left ->
                Text(
                    text = left,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        SmoothBar(fraction = run.fraction, resetKey = run.totalFiles, height = 8.dp, modifier = Modifier.padding(top = 6.dp))
        Text(
            text = TransferText.run(context, run),
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.padding(top = 4.dp),
        )
    }
}

/**
 * Progress bar that glides between the values published every second, so that it moves steadily instead of jumping;
 * a new [resetKey] (next file) starts it again without sliding back.
 */
@Composable
fun SmoothBar(
    fraction: Float,
    resetKey: Any?,
    height: Dp,
    modifier: Modifier = Modifier,
    color: Color = MaterialTheme.colorScheme.primary,
    trackColor: Color = MaterialTheme.colorScheme.primaryContainer,
) {
    val animated = remember(resetKey) { Animatable(fraction) }
    LaunchedEffect(resetKey, fraction) {
        animated.animateTo(fraction.coerceIn(0f, 1f), tween(durationMillis = 900, easing = LinearEasing))
    }
    LinearProgressIndicator(
        progress = { animated.value },
        modifier = modifier
            .fillMaxWidth()
            .height(height)
            .clip(RoundedCornerShape(height / 2)),
        color = color,
        trackColor = trackColor,
        gapSize = 0.dp,
        drawStopIndicator = {},
    )
}

private data class HeadlineTexts(val icon: ImageVector, val title: String, val detail: String?)

@Composable
private fun headlineTexts(headline: BackupHeadline?, pcName: String, nowMs: Long): HeadlineTexts = when (headline) {
    null -> HeadlineTexts(Icons.Outlined.Schedule, stringResource(R.string.backup_title), stringResource(R.string.backup_reading))
    is BackupHeadline.Running -> HeadlineTexts(Icons.Outlined.CloudUpload, stringResource(R.string.backup_running_title), null)
    BackupHeadline.Paused -> HeadlineTexts(
        Icons.Outlined.PauseCircle,
        stringResource(R.string.backup_paused_title),
        stringResource(R.string.backup_paused_detail),
    )
    is BackupHeadline.WaitingNetwork -> if (headline.mobileDataAllowed) {
        HeadlineTexts(Icons.Outlined.WifiOff, stringResource(R.string.backup_network_title), stringResource(R.string.backup_network_detail))
    } else {
        HeadlineTexts(Icons.Outlined.WifiOff, stringResource(R.string.backup_wifi_title), stringResource(R.string.backup_wifi_detail))
    }
    BackupHeadline.WaitingCharging -> HeadlineTexts(
        Icons.Outlined.BatteryChargingFull,
        stringResource(R.string.backup_charging_title),
        stringResource(R.string.backup_charging_detail),
    )
    is BackupHeadline.Unreachable -> HeadlineTexts(
        Icons.Outlined.WarningAmber,
        stringResource(R.string.backup_unreachable_title, pcName) + (headline.sinceMs?.let { " " + since(it, nowMs) } ?: ""),
        listOfNotNull(
            headline.address?.let { stringResource(R.string.status_unreachable_detail, it) },
            headline.nextAttemptMs?.let { stringResource(R.string.backup_next_attempt_at, clock(it)) },
        ).joinToString(" · ").ifEmpty { null },
    )
    BackupHeadline.DestinationUnavailable -> HeadlineTexts(
        Icons.Outlined.WarningAmber,
        stringResource(R.string.backup_destination_title, pcName),
        stringResource(R.string.backup_destination_detail),
    )
    BackupHeadline.DiskFull -> HeadlineTexts(
        Icons.Outlined.WarningAmber,
        stringResource(R.string.backup_disk_full_title, pcName),
        stringResource(R.string.backup_disk_full_detail),
    )
    is BackupHeadline.Failures -> HeadlineTexts(
        Icons.Outlined.ErrorOutline,
        pluralStringResource(R.plurals.status_errors_title, headline.count, headline.count),
        headline.nextRetryMs?.takeIf { it > nowMs }?.let { stringResource(R.string.backup_failures_detail_at, clock(it)) }
            ?: stringResource(R.string.backup_failures_detail_later),
    )
    is BackupHeadline.Pending -> HeadlineTexts(
        Icons.Outlined.Schedule,
        pluralStringResource(R.plurals.status_pending_title, headline.count, headline.count),
        headline.nextRunMs?.let { stringResource(R.string.backup_pending_next_at, clock(it)) }
            ?: stringResource(R.string.backup_pending_soon),
    )
    is BackupHeadline.AllSaved -> HeadlineTexts(
        Icons.Outlined.CheckCircle,
        stringResource(R.string.status_ok_title),
        headline.checkedMs?.let { stringResource(R.string.backup_checked, moment(it)) },
    )
    BackupHeadline.Empty -> HeadlineTexts(
        Icons.Outlined.Image,
        stringResource(R.string.status_empty_title),
        stringResource(R.string.status_empty_detail),
    )
}
