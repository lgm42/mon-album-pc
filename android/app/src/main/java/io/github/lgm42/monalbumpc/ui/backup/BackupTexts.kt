package io.github.lgm42.monalbumpc.ui.backup

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.backup.Elapsed
import io.github.lgm42.monalbumpc.backup.FailedEntry
import io.github.lgm42.monalbumpc.data.media.ErrorReasons
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.Locale
import kotlinx.coroutines.delay

private val clockFormat = DateTimeFormatter.ofPattern("HH:mm", Locale.FRENCH)
private val dayFormat = DateTimeFormatter.ofPattern("d MMMM", Locale.FRENCH)

/** The current time, ticking every [periodMs], so that "depuis 12 min" moves on by itself. */
@Composable
fun rememberNow(periodMs: Long = 30_000L): Long {
    val now by produceState(System.currentTimeMillis(), periodMs) {
        while (true) {
            delay(periodMs)
            value = System.currentTimeMillis()
        }
    }
    return now
}

/** "23:25". */
fun clock(timeMs: Long): String = clockFormat.format(Instant.ofEpochMilli(timeMs).atZone(ZoneId.systemDefault()))

/** "à 23:25", "hier à 23:25", "demain à 08:10", "le 12 novembre à 23:25". */
@Composable
fun moment(timeMs: Long): String {
    val zone = ZoneId.systemDefault()
    val then = Instant.ofEpochMilli(timeMs).atZone(zone)
    val today = LocalDate.now(zone)
    val time = clockFormat.format(then)
    return when (then.toLocalDate()) {
        today -> stringResource(R.string.time_at, time)
        today.minusDays(1) -> stringResource(R.string.time_yesterday_at, time)
        today.plusDays(1) -> stringResource(R.string.time_tomorrow_at, time)
        else -> stringResource(R.string.time_day_at, dayFormat.format(then), time)
    }
}

/** "depuis 12 min". */
@Composable
fun since(thenMs: Long, nowMs: Long): String = when (val e = Elapsed.between(thenMs, nowMs)) {
    Elapsed.JustNow -> stringResource(R.string.since_just_now)
    is Elapsed.Minutes -> pluralStringResource(R.plurals.since_minutes, e.count, e.count)
    is Elapsed.Hours -> pluralStringResource(R.plurals.since_hours, e.count, e.count)
    is Elapsed.Days -> pluralStringResource(R.plurals.since_days, e.count, e.count)
}

/** Why a file failed, short: "NAS a répondu : « Rangement impossible »". */
@Composable
fun failureReason(entry: FailedEntry, pcName: String): String = when {
    entry.reason == ErrorReasons.SERVER && !entry.message.isNullOrBlank() ->
        stringResource(R.string.backup_failure_refused, pcName, entry.message.trimEnd('.'))
    entry.reason == ErrorReasons.UNREADABLE -> stringResource(R.string.backup_failure_unreadable)
    entry.reason == ErrorReasons.HASH_MISMATCH -> stringResource(R.string.backup_failure_hash)
    entry.reason == ErrorReasons.DISK_FULL -> stringResource(R.string.backup_failure_disk_full)
    entry.reason == ErrorReasons.DESTINATION_UNAVAILABLE -> stringResource(R.string.backup_failure_destination)
    else -> stringResource(R.string.backup_failure_server, pcName)
}

/** "nouvel essai à 23:40", or "à la prochaine sauvegarde" once its time has come. */
@Composable
fun nextAttempt(retryAfterMs: Long?, nowMs: Long): String =
    if (retryAfterMs != null && retryAfterMs > nowMs) {
        stringResource(R.string.backup_failure_next_at, moment(retryAfterMs))
    } else {
        stringResource(R.string.backup_failure_next_run)
    }
