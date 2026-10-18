package io.github.lgm42.monalbumpc.ui.photos

import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceMedia
import java.time.Instant
import java.time.YearMonth
import java.time.ZoneId
import java.time.format.TextStyle
import java.util.Locale

/** The five badges of the gallery (design README §2). */
enum class BadgeState { SAVED, PENDING, UPLOADING, ERROR, UNTRACKED }

enum class GalleryFilter { ALL, PENDING, ERRORS, UNTRACKED }

data class GalleryItem(
    val media: DeviceMedia,
    val state: BadgeState,
    val folderName: String,
    val errorReason: String? = null,
    val savedAtMs: Long? = null,
)

sealed interface GalleryCell {
    val key: String

    data class Header(val month: YearMonth, val label: String, val count: Int) : GalleryCell {
        override val key: String
            get() = "h-$month"
    }

    data class Item(val item: GalleryItem) : GalleryCell {
        override val key: String
            get() = "m-${item.media.id}"
    }
}

object GalleryModel {

    private val french: Locale = Locale.FRENCH

    fun badge(tracked: Boolean, state: MediaState?, uploadingId: Long?, mediaId: Long): BadgeState = when {
        !tracked -> BadgeState.UNTRACKED
        uploadingId == mediaId || state == MediaState.UPLOADING -> BadgeState.UPLOADING
        state == MediaState.SAVED -> BadgeState.SAVED
        state == MediaState.ERROR -> BadgeState.ERROR
        // Not indexed yet: will be picked up by the next backup.
        else -> BadgeState.PENDING
    }

    fun matches(filter: GalleryFilter, state: BadgeState): Boolean = when (filter) {
        GalleryFilter.ALL -> true
        GalleryFilter.PENDING -> state == BadgeState.PENDING || state == BadgeState.UPLOADING
        GalleryFilter.ERRORS -> state == BadgeState.ERROR
        GalleryFilter.UNTRACKED -> state == BadgeState.UNTRACKED
    }

    fun counts(items: List<GalleryItem>): Map<GalleryFilter, Int> =
        GalleryFilter.entries.associateWith { filter -> items.count { matches(filter, it.state) } }

    /** "Octobre 2026". */
    fun monthLabel(month: YearMonth, locale: Locale = french): String {
        val name = month.month.getDisplayName(TextStyle.FULL_STANDALONE, locale)
        return name.replaceFirstChar { it.titlecase(locale) } + " " + month.year
    }

    fun monthOf(epochMillis: Long, zone: ZoneId): YearMonth =
        YearMonth.from(Instant.ofEpochMilli(epochMillis).atZone(zone))

    /** Newest first, a header before each month. */
    fun cells(items: List<GalleryItem>, zone: ZoneId = ZoneId.systemDefault()): List<GalleryCell> {
        val sorted = items.sortedWith(compareByDescending<GalleryItem> { it.media.sortDateMs }.thenByDescending { it.media.id })
        val result = ArrayList<GalleryCell>(sorted.size + 24)
        var index = 0
        while (index < sorted.size) {
            val month = monthOf(sorted[index].media.sortDateMs, zone)
            var end = index
            while (end < sorted.size && monthOf(sorted[end].media.sortDateMs, zone) == month) end++
            result.add(GalleryCell.Header(month, monthLabel(month), end - index))
            for (i in index until end) result.add(GalleryCell.Item(sorted[i]))
            index = end
        }
        return result
    }

    /** "1:42", or "1:02:03" for long videos. */
    fun duration(durationMs: Long): String {
        val totalSeconds = (durationMs / 1000).coerceAtLeast(0)
        val hours = totalSeconds / 3600
        val minutes = (totalSeconds % 3600) / 60
        val seconds = totalSeconds % 60
        return if (hours > 0) {
            String.format(Locale.ROOT, "%d:%02d:%02d", hours, minutes, seconds)
        } else {
            String.format(Locale.ROOT, "%d:%02d", minutes, seconds)
        }
    }
}
