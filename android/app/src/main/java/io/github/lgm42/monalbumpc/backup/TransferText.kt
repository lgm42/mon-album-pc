package io.github.lgm42.monalbumpc.backup

import android.content.Context
import io.github.lgm42.monalbumpc.R

/** Words of a running backup, shared by the banner, the Sauvegarde tab and the notification. */
object TransferText {

    /** "Recherche de NAS…", "Comparaison avec NAS…", "Préparation 3 / 20", "Envoi 3 / 20". */
    fun title(t: TextSource, done: Int, total: Int, activity: RunActivity, pcName: String): String {
        val position = if (total > 0) (done + 1).coerceAtMost(total) else 0
        return when (activity.phase) {
            BackupPhase.SEARCHING -> t.string(R.string.status_searching_title, pcName)
            BackupPhase.COMPARING -> t.string(R.string.status_comparing_title, pcName)
            BackupPhase.PREPARING -> t.string(R.string.status_preparing_title, position, total)
            BackupPhase.UPLOADING -> t.string(R.string.status_sending_title, position, total)
        }
    }

    /** "45 Mo / 120 Mo"; null outside of an upload. */
    fun fileBytes(t: TextSource, activity: RunActivity): String? {
        if (activity.phase != BackupPhase.UPLOADING || activity.fileSize <= 0) return null
        return t.string(R.string.transfer_bytes, t.size(activity.fileBytes), t.size(activity.fileSize))
    }

    /** "45 Mo / 120 Mo · 2,1 Mo/s · encore 36 s"; null outside of an upload. */
    fun bytes(t: TextSource, activity: RunActivity): String? {
        val parts = mutableListOf(fileBytes(t, activity) ?: return null)
        pace(t, activity)?.let { parts += it }
        return parts.joinToString(" · ")
    }

    /** "2,1 Mo/s · encore 36 s", or null before the pace is known. */
    fun pace(t: TextSource, activity: RunActivity): String? {
        val parts = mutableListOf<String>()
        if (activity.bytesPerSecond > 0) parts += t.string(R.string.transfer_pace, t.size(activity.bytesPerSecond))
        activity.secondsLeft?.let { seconds ->
            parts += if (seconds < 60) {
                t.string(R.string.transfer_left_seconds, seconds.toInt())
            } else {
                t.string(R.string.transfer_left_minutes, ((seconds + 59) / 60).toInt())
            }
        }
        return parts.takeIf { it.isNotEmpty() }?.joinToString(" · ")
    }

    /** "12 / 52 fichiers · 340 Mo / 1,2 Go". */
    fun run(t: TextSource, run: RunTotals): String {
        val files = t.plural(R.plurals.transfer_run_files, run.totalFiles, run.doneFiles, run.totalFiles)
        if (run.totalBytes <= 0) return files
        return files + " · " + t.string(R.string.transfer_bytes, t.size(run.doneBytes), t.size(run.totalBytes))
    }

    /** "environ 12 min restantes", or null while unknown. */
    fun runLeft(t: TextSource, run: RunTotals): String? {
        val seconds = run.secondsLeft ?: return null
        val minutes = (seconds + 59) / 60
        return when {
            seconds < 60 -> t.string(R.string.transfer_run_left_soon)
            minutes < 60 -> t.string(R.string.transfer_run_left_minutes, minutes.toInt())
            else -> t.string(R.string.transfer_run_left_hours, (minutes / 60).toInt(), (minutes % 60).toInt())
        }
    }

    // Same, from a Context (screens).

    fun title(context: Context, done: Int, total: Int, activity: RunActivity, pcName: String): String =
        title(ContextTextSource(context), done, total, activity, pcName)

    fun fileBytes(context: Context, activity: RunActivity): String? = fileBytes(ContextTextSource(context), activity)

    fun bytes(context: Context, activity: RunActivity): String? = bytes(ContextTextSource(context), activity)

    fun run(context: Context, run: RunTotals): String = run(ContextTextSource(context), run)

    fun runLeft(context: Context, run: RunTotals): String? = runLeft(ContextTextSource(context), run)

    fun size(context: Context, bytes: Long): String = ContextTextSource(context).size(bytes)
}
