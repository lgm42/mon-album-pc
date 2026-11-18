package io.github.lgm42.monalbumpc.backup

import android.content.Context
import android.text.format.Formatter
import io.github.lgm42.monalbumpc.R

/** Words of a running backup, shared by the banner and the notification. */
object TransferText {

    /** "Recherche de NAS…", "Comparaison avec NAS…", "Préparation 3 / 20", "Envoi 3 / 20". */
    fun title(context: Context, done: Int, total: Int, activity: RunActivity, pcName: String): String {
        val position = if (total > 0) (done + 1).coerceAtMost(total) else 0
        return when (activity.phase) {
            BackupPhase.SEARCHING -> context.getString(R.string.status_searching_title, pcName)
            BackupPhase.COMPARING -> context.getString(R.string.status_comparing_title, pcName)
            BackupPhase.PREPARING -> context.getString(R.string.status_preparing_title, position, total)
            BackupPhase.UPLOADING -> context.getString(R.string.status_sending_title, position, total)
        }
    }

    /** "45 Mo / 120 Mo · 2,1 Mo/s · encore 36 s"; null outside of an upload. */
    fun bytes(context: Context, activity: RunActivity): String? {
        if (activity.phase != BackupPhase.UPLOADING || activity.fileSize <= 0) return null
        val parts = mutableListOf(
            context.getString(
                R.string.transfer_bytes,
                Formatter.formatShortFileSize(context, activity.fileBytes),
                Formatter.formatShortFileSize(context, activity.fileSize),
            ),
        )
        if (activity.bytesPerSecond > 0) {
            parts += context.getString(R.string.transfer_pace, Formatter.formatShortFileSize(context, activity.bytesPerSecond))
        }
        activity.secondsLeft?.let { seconds ->
            parts += if (seconds < 60) {
                context.getString(R.string.transfer_left_seconds, seconds.toInt())
            } else {
                context.getString(R.string.transfer_left_minutes, ((seconds + 59) / 60).toInt())
            }
        }
        return parts.joinToString(" · ")
    }
}
