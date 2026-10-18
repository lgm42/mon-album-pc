package io.github.lgm42.monalbumpc.ui.photos

import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceMedia
import java.time.YearMonth
import java.time.ZoneOffset
import org.junit.Assert.assertEquals
import org.junit.Test

class GalleryModelTest {

    @Test
    fun `month labels are capitalized French`() {
        assertEquals("Octobre 2026", GalleryModel.monthLabel(YearMonth.of(2026, 10)))
        assertEquals("Février 2025", GalleryModel.monthLabel(YearMonth.of(2025, 2)))
        assertEquals("Août 2024", GalleryModel.monthLabel(YearMonth.of(2024, 8)))
    }

    @Test
    fun `durations`() {
        assertEquals("0:05", GalleryModel.duration(5_400))
        assertEquals("1:42", GalleryModel.duration(102_000))
        assertEquals("1:02:03", GalleryModel.duration(3_723_000))
    }

    @Test
    fun `badges`() {
        assertEquals(BadgeState.UNTRACKED, GalleryModel.badge(false, MediaState.SAVED, null, 1))
        assertEquals(BadgeState.PENDING, GalleryModel.badge(true, null, null, 1))
        assertEquals(BadgeState.UPLOADING, GalleryModel.badge(true, MediaState.PENDING, 1, 1))
        assertEquals(BadgeState.SAVED, GalleryModel.badge(true, MediaState.SAVED, 2, 1))
        assertEquals(BadgeState.ERROR, GalleryModel.badge(true, MediaState.ERROR, null, 1))
        assertEquals(true, GalleryModel.matches(GalleryFilter.PENDING, BadgeState.UPLOADING))
        assertEquals(false, GalleryModel.matches(GalleryFilter.ERRORS, BadgeState.PENDING))
    }

    private fun item(id: Long, takenMs: Long, state: BadgeState = BadgeState.SAVED) = GalleryItem(
        media = DeviceMedia(
            id = id, uri = "content://x/$id", volume = "external_primary", relativePath = "DCIM/Camera/",
            displayName = "$id.jpg", size = 1, dateModifiedMs = 1, dateTakenMs = takenMs, mime = "image/jpeg",
            isVideo = false, durationMs = 0,
        ),
        state = state,
        folderName = "Appareil photo",
    )

    @Test
    fun `cells are grouped by month, newest first`() {
        // 2026-10-17, 2026-10-01, 2026-09-30 (UTC)
        val oct17 = 1_792_195_200_000L
        val oct1 = 1_790_812_800_000L
        val sep30 = 1_790_726_400_000L
        val cells = GalleryModel.cells(listOf(item(1, sep30), item(2, oct17), item(3, oct1)), ZoneOffset.UTC)
        assertEquals(5, cells.size)
        val first = cells[0] as GalleryCell.Header
        assertEquals("Octobre 2026", first.label)
        assertEquals(2, first.count)
        assertEquals(2L, (cells[1] as GalleryCell.Item).item.media.id)
        assertEquals(3L, (cells[2] as GalleryCell.Item).item.media.id)
        assertEquals("Septembre 2026", (cells[3] as GalleryCell.Header).label)
        assertEquals(1L, (cells[4] as GalleryCell.Item).item.media.id)
    }

    @Test
    fun `counts per filter`() {
        val counts = GalleryModel.counts(
            listOf(
                item(1, 1, BadgeState.SAVED),
                item(2, 1, BadgeState.PENDING),
                item(3, 1, BadgeState.UPLOADING),
                item(4, 1, BadgeState.ERROR),
                item(5, 1, BadgeState.UNTRACKED),
            ),
        )
        assertEquals(5, counts[GalleryFilter.ALL])
        assertEquals(2, counts[GalleryFilter.PENDING])
        assertEquals(1, counts[GalleryFilter.ERRORS])
        assertEquals(1, counts[GalleryFilter.UNTRACKED])
    }
}
