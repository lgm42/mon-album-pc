package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceMedia
import io.github.lgm42.monalbumpc.net.ApiError
import java.io.ByteArrayInputStream
import java.io.FilterInputStream
import java.io.IOException
import java.io.InputStream
import java.time.ZoneId
import java.time.ZoneOffset
import okio.Buffer
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class BackupLogicTest {

    @Test
    fun `iso dates carry the offset, to the second`() {
        val paris = ZoneId.of("Europe/Paris")
        // 2026-10-17T11:17:05.789Z
        val ms = 1_792_235_825_789L
        assertEquals("2026-10-17T13:17:05+02:00", IsoDates.format(ms, paris))
        assertEquals("2026-10-17T11:17:05Z", IsoDates.format(ms, ZoneOffset.UTC))
        assertEquals(null, IsoDates.formatOrNull(null, paris))
        assertEquals(null, IsoDates.formatOrNull(0, paris))
    }

    @Test
    fun `sha256 is lowercase hex`() {
        val hex = Sha256.hex(ByteArrayInputStream("abc".toByteArray()))
        assertEquals("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hex)
        val empty = Sha256.hex(ByteArrayInputStream(ByteArray(0)))
        assertEquals("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", empty)
    }

    private val data = ByteArray(300_000) { (it % 251).toByte() }

    @Test
    fun `upload body sends the bytes after the offset`() {
        for (offset in listOf(0L, 1L, 65_536L, 299_999L, 300_000L)) {
            val body = OffsetStreamBody(data.size.toLong(), offset, { ByteArrayInputStream(data) })
            assertEquals(data.size - offset, body.contentLength())
            val sink = Buffer()
            body.writeTo(sink)
            assertArrayEquals(data.copyOfRange(offset.toInt(), data.size), sink.readByteArray())
        }
    }

    @Test
    fun `upload body copes with streams that refuse to skip`() {
        val stubborn = { object : FilterInputStream(ByteArrayInputStream(data)) {
            override fun skip(n: Long): Long = 0
        } as InputStream }
        var lastReported = -1L
        val body = OffsetStreamBody(data.size.toLong(), 100_000L, stubborn, { lastReported = it })
        val sink = Buffer()
        body.writeTo(sink)
        assertArrayEquals(data.copyOfRange(100_000, data.size), sink.readByteArray())
        assertEquals(data.size.toLong(), lastReported)
    }

    @Test
    fun `upload body fails when the file shrank`() {
        val body = OffsetStreamBody(data.size + 10L, 0L, { ByteArrayInputStream(data) })
        var failed = false
        try {
            body.writeTo(Buffer())
        } catch (e: IOException) {
            failed = true
        }
        assertTrue(failed)
    }

    @Test
    fun `skipFully fails past the end`() {
        var failed = false
        try {
            Streams.skipFully(ByteArrayInputStream(ByteArray(10)), 11)
        } catch (e: IOException) {
            failed = true
        }
        assertTrue(failed)
    }

    @Test
    fun `protocol errors map to worker actions`() {
        assertEquals(FailureAction.RETRY_LATER, BackupErrors.actionFor(ApiError.Network("timeout")))
        assertEquals(FailureAction.RETRY_LATER, BackupErrors.actionFor(ApiError.Http(500, null)))
        assertEquals(FailureAction.STOP_DESTINATION_UNAVAILABLE, BackupErrors.actionFor(ApiError.DestinationUnavailable))
        assertEquals(FailureAction.STOP_DISK_FULL, BackupErrors.actionFor(ApiError.DiskFull))
        assertEquals(FailureAction.STOP_BUSY, BackupErrors.actionFor(ApiError.BusyUsb))
        assertEquals(FailureAction.UNPAIR, BackupErrors.actionFor(ApiError.DeviceUnknown))
        assertEquals(FailureAction.RESUME_AT_OFFSET, BackupErrors.actionFor(ApiError.OffsetMismatch(42)))
        assertEquals(FailureAction.RESTART_UPLOAD, BackupErrors.actionFor(ApiError.HashMismatch))
        assertEquals(FailureAction.RESTART_UPLOAD, BackupErrors.actionFor(ApiError.Http(404, null)))
        assertEquals(FailureAction.FAIL_ITEM, BackupErrors.actionFor(ApiError.Http(400, "invalid_request")))
        assertEquals(FailureAction.STOP_PROTOCOL, BackupErrors.actionFor(ApiError.ProtocolUnsupported))
    }

    @Test
    fun `banner priorities`() {
        fun banner(
            running: Boolean = false,
            issue: BackupIssue = BackupIssue.NONE,
            errors: Int = 0,
            pending: Int = 0,
            tracked: Int = 10,
        ) = BannerLogic.compute(running, 3, 9, issue, 1000L, errors, pending, tracked, 500L)

        assertEquals(BannerState.Running(3, 9), banner(running = true, issue = BackupIssue.DISK_FULL, pending = 5))
        assertEquals(BannerState.DestinationUnavailable, banner(issue = BackupIssue.DESTINATION_UNAVAILABLE, pending = 1))
        assertEquals(BannerState.DiskFull, banner(issue = BackupIssue.DISK_FULL, errors = 1))
        assertEquals(BannerState.PcUnreachable(1000L), banner(issue = BackupIssue.PC_UNREACHABLE, pending = 2))
        assertEquals(BannerState.Errors(2), banner(errors = 2, pending = 4))
        assertEquals(BannerState.Pending(4), banner(pending = 4))
        assertEquals(BannerState.Empty, banner(tracked = 0))
        assertEquals(BannerState.AllSaved(500L), banner())
        // Nothing waiting: an old issue does not hide "everything is saved".
        assertEquals(BannerState.AllSaved(500L), banner(issue = BackupIssue.PC_UNREACHABLE))
    }

    @Test
    fun `elapsed time buckets`() {
        val now = 10L * 24 * 3_600_000
        assertEquals(Elapsed.JustNow, Elapsed.between(now - 30_000, now))
        assertEquals(Elapsed.Minutes(5), Elapsed.between(now - 5 * 60_000, now))
        assertEquals(Elapsed.Hours(2), Elapsed.between(now - 2 * 3_600_000 - 1, now))
        assertEquals(Elapsed.Days(3), Elapsed.between(now - 3 * 24 * 3_600_000, now))
        assertEquals(Elapsed.JustNow, Elapsed.between(now + 5_000, now))
    }

    private fun media(id: Long, folder: String = "DCIM/Camera/", size: Long = 100, modified: Long = 1_000) = DeviceMedia(
        id = id,
        uri = "content://media/external_primary/images/media/$id",
        volume = "external_primary",
        relativePath = folder,
        displayName = "IMG_$id.jpg",
        size = size,
        dateModifiedMs = modified,
        dateTakenMs = 900,
        mime = "image/jpeg",
        isVideo = false,
        durationMs = 0,
    )

    @Test
    fun `index sync adds, resets, moves and removes`() {
        val camera = "external_primary|DCIM/Camera/"
        val saved = IndexSync.newEntity(media(1)).copy(sha256 = "aa", state = MediaState.SAVED, savedAt = 5)
        val changed = IndexSync.newEntity(media(2)).copy(sha256 = "bb", state = MediaState.SAVED)
        val moved = IndexSync.newEntity(media(3)).copy(sha256 = "cc", state = MediaState.SAVED)
        val gone = IndexSync.newEntity(media(4)).copy(sha256 = "dd")
        val untracked = IndexSync.newEntity(media(5, folder = "Download/"))

        val current = listOf(
            media(1),
            media(2, size = 200),
            media(3).copy(displayName = "renamed.jpg"),
            media(5, folder = "Download/"),
            media(6),
        )
        val diff = IndexSync.diff(listOf(saved, changed, moved, gone, untracked), current, setOf(camera))

        val upserts = diff.upserts.associateBy { it.mediaStoreId }
        assertEquals(setOf(2L, 3L, 6L), upserts.keys)
        assertEquals(null, upserts.getValue(2).sha256)
        assertEquals(MediaState.PENDING, upserts.getValue(2).state)
        assertEquals("cc", upserts.getValue(3).sha256)
        assertEquals(MediaState.SAVED, upserts.getValue(3).state)
        assertEquals("renamed.jpg", upserts.getValue(3).displayName)
        assertEquals(MediaState.PENDING, upserts.getValue(6).state)
        assertEquals(setOf(4L, 5L), diff.deletions.toSet())
    }
}
