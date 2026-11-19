package io.github.lgm42.monalbumpc.backup

import android.content.Context
import android.text.format.Formatter
import androidx.annotation.PluralsRes
import androidx.annotation.StringRes
import io.github.lgm42.monalbumpc.R

/** The app's words, looked up by resource id: from a [Context] in the app, from strings.xml in the unit tests. */
interface TextSource {
    fun string(@StringRes id: Int, vararg args: Any): String
    fun plural(@PluralsRes id: Int, count: Int, vararg args: Any): String

    /** "45 Mo", "1,2 Go". */
    fun size(bytes: Long): String
}

class ContextTextSource(context: Context) : TextSource {
    private val context = context.applicationContext ?: context

    override fun string(id: Int, vararg args: Any): String = context.getString(id, *args)

    override fun plural(id: Int, count: Int, vararg args: Any): String = context.resources.getQuantityString(id, count, *args)

    override fun size(bytes: Long): String = Formatter.formatShortFileSize(context, bytes)
}

/** What a notification shows. [title] and [text] are read collapsed, at a glance; [expanded] holds the details. */
data class NotificationText(
    val title: String,
    val text: String?,
    /** Shown small in the header (the server's name). */
    val subText: String? = null,
    val expanded: String? = null,
)

/** Shortens names in the middle, so that both their start and their end (extension, number) stay readable. */
object ShortText {

    /** "IMG-2023…WA0007.mp4": at most [max] characters, keeping the extension. */
    fun middle(text: String, max: Int): String {
        if (text.length <= max) return text
        if (max <= 1) return ELLIPSIS
        val keep = max - 1
        val dot = text.lastIndexOf('.')
        val extension = if (dot > 0 && text.length - dot <= MAX_EXTENSION) text.length - dot else 0
        val tail = maxOf(keep / 2, extension + 2).coerceAtMost(keep - 1).coerceAtLeast(0)
        val head = keep - tail
        return text.take(head) + ELLIPSIS + text.takeLast(tail)
    }

    /** [build] with [name] shortened just enough for the whole to fit in [max] characters. */
    fun fit(max: Int, name: String, build: (String) -> String): String {
        val full = build(name)
        if (full.length <= max) return full
        val budget = (name.length - (full.length - max)).coerceAtLeast(1)
        return build(middle(name, budget))
    }

    const val ELLIPSIS = "…"
    private const val MAX_EXTENSION = 6
}

/**
 * Words of every notification of the app. Collapsed, a notification shows one line of title and one of text on a
 * ~360 dp phone: the essential comes first and fits ([TITLE_MAX], [TEXT_MAX]); the server's name goes in the header,
 * the details in the expanded view and in the Sauvegarde tab.
 */
object NotificationTexts {

    const val TITLE_MAX = 30
    const val TEXT_MAX = 40
    const val SUB_MAX = 24

    /** "Sauvegarde · 4 / 20" over "IMG_1234.jpg · 45 Mo / 120 Mo"; the bar shows the file being sent. */
    fun progress(t: TextSource, done: Int, total: Int, pcName: String, activity: RunActivity?, run: RunTotals?): NotificationText {
        val position = if (total > 0) (done + 1).coerceAtMost(total) else 0
        val sub = ShortText.middle(pcName, SUB_MAX)
        val name = activity?.fileName
        return when {
            activity?.phase == BackupPhase.UPLOADING && total > 0 -> {
                val bytes = TransferText.fileBytes(t, activity)
                val text = when {
                    name == null -> bytes
                    bytes == null -> ShortText.middle(name, TEXT_MAX)
                    else -> ShortText.fit(TEXT_MAX, name) { "$it · $bytes" }
                }
                NotificationText(
                    title = t.string(R.string.notif_title_run, position, total),
                    text = text,
                    subText = sub,
                    expanded = listOfNotNull(
                        name,
                        TransferText.bytes(t, activity),
                        run?.let { t.string(R.string.notif_total, listOfNotNull(TransferText.run(t, it), TransferText.runLeft(t, it)).joinToString(" · ")) },
                        t.string(R.string.notif_to, pcName),
                    ).joinToString("\n"),
                )
            }
            activity?.phase == BackupPhase.PREPARING && total > 0 -> NotificationText(
                title = t.string(R.string.notif_title_preparing, position, total),
                text = name?.let { ShortText.middle(it, TEXT_MAX) },
                subText = sub,
                expanded = listOfNotNull(name, t.string(R.string.notif_to, pcName)).joinToString("\n"),
            )
            activity?.phase == BackupPhase.SEARCHING -> NotificationText(
                title = ShortText.fit(TITLE_MAX, pcName) { t.string(R.string.status_searching_title, it) },
                text = null,
                subText = sub,
            )
            activity?.phase == BackupPhase.COMPARING -> NotificationText(
                title = ShortText.fit(TITLE_MAX, pcName) { t.string(R.string.status_comparing_title, it) },
                text = null,
                subText = sub,
            )
            total > 0 -> NotificationText(t.string(R.string.notif_title_run, position, total), null, sub)
            else -> NotificationText(t.string(R.string.notif_title_starting), null, sub)
        }
    }

    /** "Tout est sauvegardé" (or what is left) over "52 photos et 3 vidéos envoyées". */
    fun done(t: TextSource, photos: Int, videos: Int, pending: Int, failed: Int, pcName: String): NotificationText {
        val title = when {
            pending > 0 -> t.plural(R.plurals.notif_done_pending, pending, pending)
            failed > 0 -> t.plural(R.plurals.status_errors_title, failed, failed)
            else -> t.string(R.string.notif_done_all)
        }
        val parts = buildList {
            if (photos > 0) add(t.plural(R.plurals.notif_photos, photos, photos))
            if (videos > 0) add(t.plural(R.plurals.notif_videos, videos, videos))
        }
        val what = parts.joinToString(t.string(R.string.notif_and))
        return NotificationText(
            title = title,
            text = t.plural(R.plurals.notif_done_sent, photos + videos, what),
            subText = ShortText.middle(pcName, SUB_MAX),
            expanded = t.plural(R.plurals.notif_done, photos + videos, what, pcName),
        )
    }

    /** "Disque de NAS plein" over "Les photos attendent de la place". */
    fun issue(t: TextSource, issue: BackupIssue, pcName: String): NotificationText? = when (issue) {
        BackupIssue.DISK_FULL -> NotificationText(
            title = ShortText.fit(TITLE_MAX, pcName) { t.string(R.string.notif_disk_full_title, it) },
            text = t.string(R.string.notif_disk_full_text),
            expanded = t.string(R.string.notif_error_disk_full, pcName),
        )
        BackupIssue.DESTINATION_UNAVAILABLE -> NotificationText(
            title = t.string(R.string.notif_destination_title),
            text = ShortText.fit(TEXT_MAX, pcName) { t.string(R.string.notif_destination_text, it) },
            expanded = t.string(R.string.notif_error_destination, pcName),
        )
        else -> null
    }

    /** "Mise à jour disponible" over "Touchez pour installer la 1.5.0". */
    fun update(t: TextSource, versionName: String): NotificationText = NotificationText(
        title = t.string(R.string.notif_update_title),
        text = ShortText.fit(TEXT_MAX, versionName) { t.string(R.string.notif_update_short, it) },
        expanded = t.string(R.string.notif_update_text, versionName),
    )
}
