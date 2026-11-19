package io.github.lgm42.monalbumpc.backup

import android.content.Context
import android.text.format.Formatter
import io.github.lgm42.monalbumpc.R

/** Words of a running backup, shared by the banner, the Sauvegarde tab and the notification. */
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

    /** "45 Mo / 120 Mo"; null outside of an upload. */
    fun fileBytes(context: Context, activity: RunActivity): String? {
        if (activity.phase != BackupPhase.UPLOADING || activity.fileSize <= 0) return null
        return context.getString(R.string.transfer_bytes, size(context, activity.fileBytes), size(context, activity.fileSize))
    }

    /** "45 Mo / 120 Mo · 2,1 Mo/s · encore 36 s"; null outside of an upload. */
    fun bytes(context: Context, activity: RunActivity): String? {
        val parts = mutableListOf(fileBytes(context, activity) ?: return null)
        pace(context, activity)?.let { parts += it }
        return parts.joinToString(" · ")
    }

    /** "2,1 Mo/s · encore 36 s", or null before the pace is known. */
    fun pace(context: Context, activity: RunActivity): String? {
        val parts = mutableListOf<String>()
        if (activity.bytesPerSecond > 0) {
            parts += context.getString(R.string.transfer_pace, size(context, activity.bytesPerSecond))
        }
        activity.secondsLeft?.let { seconds ->
            parts += if (seconds < 60) {
                context.getString(R.string.transfer_left_seconds, seconds.toInt())
            } else {
                context.getString(R.string.transfer_left_minutes, ((seconds + 59) / 60).toInt())
            }
        }
        return parts.takeIf { it.isNotEmpty() }?.joinToString(" · ")
    }

    /** "12 / 52 fichiers · 340 Mo / 1,2 Go". */
    fun run(context: Context, run: RunTotals): String {
        val files = context.resources.getQuantityString(R.plurals.transfer_run_files, run.totalFiles, run.doneFiles, run.totalFiles)
        if (run.totalBytes <= 0) return files
        return files + " · " + context.getString(R.string.transfer_bytes, size(context, run.doneBytes), size(context, run.totalBytes))
    }

    /** "environ 12 min restantes", or null while unknown. */
    fun runLeft(context: Context, run: RunTotals): String? {
        val seconds = run.secondsLeft ?: return null
        val minutes = (seconds + 59) / 60
        return when {
            seconds < 60 -> context.getString(R.string.transfer_run_left_soon)
            minutes < 60 -> context.getString(R.string.transfer_run_left_minutes, minutes.toInt())
            else -> context.getString(R.string.transfer_run_left_hours, (minutes / 60).toInt(), (minutes % 60).toInt())
        }
    }

    fun size(context: Context, bytes: Long): String = Formatter.formatShortFileSize(context, bytes)
}
