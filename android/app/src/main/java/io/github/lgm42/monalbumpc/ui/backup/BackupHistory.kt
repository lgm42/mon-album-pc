package io.github.lgm42.monalbumpc.ui.backup

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyListScope
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.backup.TransferText
import io.github.lgm42.monalbumpc.data.media.BackupRunEntity
import io.github.lgm42.monalbumpc.data.media.RunEnd
import io.github.lgm42.monalbumpc.data.media.end
import io.github.lgm42.monalbumpc.ui.folders.formatCount
import io.github.lgm42.monalbumpc.ui.theme.LocalExtendedColors
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale

private val dayFormat = DateTimeFormatter.ofPattern("d MMMM", Locale.FRENCH)

/** "Historique": the last runs, how long they took, what they did and how they ended. */
internal fun LazyListScope.historyItems(history: List<BackupRunEntity>, pcName: String) {
    if (history.isEmpty()) {
        item(key = "history-empty", contentType = "note") {
            Text(
                text = stringResource(R.string.backup_empty_history),
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(horizontal = 20.dp, vertical = 12.dp),
            )
        }
        return
    }
    items(history, key = { "run-${it.id}" }, contentType = { "run" }) { run -> RunRow(run, pcName) }
}

@Composable
private fun RunRow(run: BackupRunEntity, pcName: String) {
    val context = LocalContext.current
    val end = run.end
    val problem = end != RunEnd.COMPLETED
    Surface(
        modifier = Modifier
            .padding(horizontal = 16.dp, vertical = 4.dp)
            .fillMaxWidth(),
        shape = RoundedCornerShape(22.dp),
        color = MaterialTheme.colorScheme.surface,
    ) {
        Column(
            modifier = Modifier
                .heightIn(min = 64.dp)
                .padding(horizontal = 18.dp, vertical = 12.dp),
        ) {
            Text(
                text = startedAt(run.startedAt) + " · " + duration(run.endedAt - run.startedAt),
                style = MaterialTheme.typography.titleMedium,
                color = MaterialTheme.colorScheme.onSurface,
            )
            val outcome = buildList {
                add(endText(end, pcName))
                if (run.attempts > 1) {
                    add(pluralStringResource(R.plurals.history_attempts, run.attempts, run.attempts, moment(run.endedAt)))
                }
            }.joinToString(" · ")
            Text(
                text = outcome,
                style = MaterialTheme.typography.bodyMedium,
                color = if (problem) LocalExtendedColors.current.warningIcon else LocalExtendedColors.current.primaryText,
            )
            val counts = buildList {
                if (run.sent > 0) {
                    add(
                        pluralStringResource(
                            R.plurals.history_sent,
                            run.sent,
                            formatCount(run.sent),
                            TransferText.size(context, run.sentBytes),
                        ),
                    )
                }
                if (run.recognized > 0) add(stringResource(R.string.history_recognized, formatCount(run.recognized), pcName))
                if (run.failed > 0) add(stringResource(R.string.history_failed, formatCount(run.failed)))
                if (isEmpty() && run.queued > 0) {
                    add(pluralStringResource(R.plurals.history_queued, run.queued, formatCount(run.queued)))
                }
            }
            if (counts.isNotEmpty()) {
                Text(
                    text = counts.joinToString(" · "),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun endText(end: RunEnd, pcName: String): String = when (end) {
    RunEnd.COMPLETED -> stringResource(R.string.history_end_completed)
    RunEnd.UNREACHABLE -> stringResource(R.string.history_end_unreachable, pcName)
    RunEnd.DESTINATION_UNAVAILABLE -> stringResource(R.string.history_end_destination)
    RunEnd.DISK_FULL -> stringResource(R.string.history_end_disk_full)
    RunEnd.REFUSED -> stringResource(R.string.history_end_refused, pcName)
    RunEnd.INTERRUPTED -> stringResource(R.string.history_end_interrupted)
    RunEnd.PAUSED -> stringResource(R.string.history_end_paused)
    RunEnd.BUSY -> stringResource(R.string.history_end_busy, pcName)
}

/** "Aujourd'hui à 23:10", "Hier à 23:10", "Le 12 novembre à 23:10". */
@Composable
private fun startedAt(timeMs: Long): String {
    val zone = ZoneId.systemDefault()
    val then = Instant.ofEpochMilli(timeMs).atZone(zone)
    val today = LocalDate.now(zone)
    val time = clock(timeMs)
    return when (then.toLocalDate()) {
        today -> stringResource(R.string.history_today, time)
        today.minusDays(1) -> stringResource(R.string.history_yesterday, time)
        else -> stringResource(R.string.history_day, dayFormat.format(then), time)
    }
}

/** "45 s", "3 min", "1 h 05". */
@Composable
private fun duration(ms: Long): String {
    val seconds = (ms / 1000).coerceAtLeast(0)
    return when {
        seconds < 60 -> stringResource(R.string.history_duration_seconds, seconds.toInt())
        seconds < 3600 -> stringResource(R.string.history_duration_minutes, ((seconds + 30) / 60).toInt())
        else -> stringResource(R.string.history_duration_hours, (seconds / 3600).toInt(), ((seconds % 3600) / 60).toInt())
    }
}
