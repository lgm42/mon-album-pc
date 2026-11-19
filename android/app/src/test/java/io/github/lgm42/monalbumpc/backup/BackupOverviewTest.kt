package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.data.media.MediaEntity
import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceFolders
import io.github.lgm42.monalbumpc.media.DeviceMedia
import io.github.lgm42.monalbumpc.media.FolderRules
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class BackupOverviewTest {

    private val camera = "external_primary|DCIM/Camera/"
    private val whatsApp = "external_primary|Android/media/com.whatsapp/WhatsApp/Media/WhatsApp Images/"

    private fun media(id: Long, folder: String = "DCIM/Camera/", size: Long = 100, taken: Long = id * 1_000, video: Boolean = false) =
        DeviceMedia(
            id = id,
            uri = "content://media/external_primary/images/media/$id",
            volume = FolderRules.PRIMARY_VOLUME,
            relativePath = folder,
            displayName = "IMG_$id.jpg",
            size = size,
            dateModifiedMs = taken,
            dateTakenMs = taken,
            mime = "image/jpeg",
            isVideo = video,
            durationMs = if (video) 5_000 else 0,
        )

    private fun entity(m: DeviceMedia, state: MediaState, savedAt: Long? = null, retryAfter: Long? = null, message: String? = null): MediaEntity =
        IndexSync.newEntity(m).copy(
            state = state,
            savedAt = savedAt,
            retryAfter = retryAfter,
            errorReason = if (state == MediaState.ERROR) "server" else null,
            errorMessage = message,
            failures = if (state == MediaState.ERROR) 1 else 0,
        )

    private fun summarize(media: List<DeviceMedia>, tracked: Set<String>, index: List<MediaEntity>, recentLimit: Int = 200) =
        BackupOverview.summarize(media, DeviceFolders.fromMedia(media), tracked, index, recentLimit)

    @Test
    fun `counts saved, pending and failed files with their sizes, unindexed ones as pending`() {
        val saved = media(1, size = 1_000)
        val pending = media(2, size = 200)
        val uploading = media(3, size = 300)
        val failed = media(4, size = 4_000)
        val notIndexed = media(5, size = 50)
        val untracked = media(6, folder = "Download/", size = 9_999)
        val all = listOf(saved, pending, uploading, failed, notIndexed, untracked)
        val index = listOf(
            entity(saved, MediaState.SAVED, savedAt = 10),
            entity(pending, MediaState.PENDING),
            entity(uploading, MediaState.UPLOADING),
            entity(failed, MediaState.ERROR, retryAfter = 99),
        )

        val summary = summarize(all, setOf(camera), index)

        assertEquals(Tally(1, 1_000), summary.saved)
        assertEquals(Tally(3, 550), summary.pending)
        assertEquals(Tally(1, 4_000), summary.failed)
        assertEquals(5, summary.tracked)
        assertEquals(99L, summary.nextRetryMs)
    }

    @Test
    fun `per folder progress and untracked folders`() {
        val all = listOf(
            media(1),
            media(2),
            media(3),
            media(10, folder = "Android/media/com.whatsapp/WhatsApp/Media/WhatsApp Images/"),
            media(20, folder = "Download/"),
            media(21, folder = "Download/"),
        )
        val index = listOf(
            entity(all[0], MediaState.SAVED, savedAt = 1),
            entity(all[1], MediaState.SAVED, savedAt = 2),
            entity(all[2], MediaState.ERROR),
            entity(all[3], MediaState.SAVED, savedAt = 3),
        )

        val summary = summarize(all, setOf(camera, whatsApp), index)

        val byKey = summary.folders.associateBy { it.key }
        assertEquals(setOf(camera, whatsApp), byKey.keys)
        assertEquals(FolderProgress(camera, FolderRules.CAMERA, 2, 3, 1, all[2].uri), byKey.getValue(camera))
        assertEquals(1f, byKey.getValue(whatsApp).fraction)
        assertEquals(listOf(UntrackedFolder("external_primary|Download/", FolderRules.DOWNLOADS, 2)), summary.untracked)
    }

    @Test
    fun `the queue goes newest first, failures by next attempt, recent files by save time`() {
        val all = (1L..6L).map { media(it) }
        val index = listOf(
            entity(all[0], MediaState.PENDING),
            entity(all[1], MediaState.ERROR, retryAfter = 500, message = "Rangement impossible"),
            entity(all[2], MediaState.ERROR, retryAfter = 100),
            entity(all[3], MediaState.SAVED, savedAt = 30),
            entity(all[4], MediaState.SAVED, savedAt = 50),
            entity(all[5], MediaState.SAVED, savedAt = 40),
        )
        // 7 is not indexed yet and the newest of all.
        val summary = summarize(all + media(7), setOf(camera), index, recentLimit = 2)

        assertEquals(listOf(7L, 1L), summary.queue.map { it.id })
        assertEquals(FolderRules.CAMERA, summary.queue.first().folderName)
        assertEquals(listOf(3L, 2L), summary.failures.map { it.id })
        assertEquals("Rangement impossible", summary.failures[1].message)
        assertEquals(listOf(5L, 6L), summary.recent.map { it.id })
    }

    @Test
    fun `nothing tracked gives an empty summary`() {
        val summary = summarize(listOf(media(1)), emptySet(), emptyList())
        assertEquals(0, summary.tracked)
        assertEquals(1, summary.untracked.size)
        assertNull(summary.nextRetryMs)
    }

    private fun headline(
        pending: Int = 0,
        failed: Int = 0,
        tracked: Int = 10,
        running: Boolean = false,
        activity: RunActivity? = null,
        conditions: DeviceConditions = DeviceConditions(),
        chargingOnly: Boolean = false,
        mobileData: Boolean = false,
        issue: BackupIssue = BackupIssue.NONE,
        paused: Boolean = false,
    ): BackupHeadline {
        val saved = tracked - pending - failed
        val summary = BackupSummary(
            saved = Tally(saved.coerceAtLeast(0), 0),
            pending = Tally(pending, 0),
            failed = Tally(failed, 0),
            failures = if (failed > 0) listOf(FailedEntry(1, "a.jpg", "", 1, false, "server", null, 700, 1)) else emptyList(),
        )
        return BackupOverview.headline(
            summary = summary,
            running = running,
            done = 2,
            total = 5,
            activity = activity,
            run = null,
            paused = paused,
            conditions = conditions,
            chargingOnly = chargingOnly,
            mobileData = mobileData,
            issue = issue,
            issueSinceMs = 1_000,
            issueDetail = "nas.local:48620",
            checkedMs = 900,
            nextRunMs = 5_000,
        )
    }

    @Test
    fun `headline priorities`() {
        val sending = RunActivity(BackupPhase.UPLOADING, "IMG.jpg", 1, 2)
        val offline = DeviceConditions(connected = false, unmetered = false)
        val mobile = DeviceConditions(connected = true, unmetered = false)

        // The pause wins, even over a run that is still stopping.
        assertEquals(BackupHeadline.Paused, headline(pending = 3, running = true, activity = sending, paused = true))
        assertEquals(BackupHeadline.Paused, headline(paused = true))
        assertEquals(BackupHeadline.Running(2, 5, sending, null), headline(pending = 3, running = true, activity = sending, issue = BackupIssue.DISK_FULL))
        // Reading the phone's photos is a run worth showing only when something waits.
        assertEquals(BackupHeadline.Running(2, 5, null, null), headline(pending = 3, running = true))
        assertEquals(BackupHeadline.AllSaved(900), headline(running = true))
        assertEquals(BackupHeadline.Empty, headline(tracked = 0))
        assertEquals(BackupHeadline.AllSaved(900), headline(issue = BackupIssue.PC_UNREACHABLE, conditions = offline))

        assertEquals(BackupHeadline.DiskFull, headline(pending = 1, issue = BackupIssue.DISK_FULL, conditions = offline))
        assertEquals(BackupHeadline.DestinationUnavailable, headline(failed = 1, issue = BackupIssue.DESTINATION_UNAVAILABLE))
        assertEquals(BackupHeadline.WaitingNetwork(false), headline(pending = 1, conditions = mobile, issue = BackupIssue.PC_UNREACHABLE))
        assertEquals(BackupHeadline.WaitingNetwork(true), headline(pending = 1, conditions = offline, mobileData = true))
        assertEquals(BackupHeadline.Pending(1, 5_000), headline(pending = 1, conditions = mobile, mobileData = true))
        assertEquals(BackupHeadline.WaitingCharging, headline(pending = 1, chargingOnly = true, issue = BackupIssue.PC_UNREACHABLE))
        assertEquals(BackupHeadline.Pending(1, 5_000), headline(pending = 1, chargingOnly = true, conditions = DeviceConditions(charging = true)))
        assertEquals(BackupHeadline.Unreachable(1_000, "nas.local:48620", 5_000), headline(pending = 1, issue = BackupIssue.PC_UNREACHABLE))
        assertEquals(BackupHeadline.Pending(2, 5_000), headline(pending = 2, failed = 1))
        assertEquals(BackupHeadline.Failures(1, 700), headline(failed = 1))
    }

    @Test
    fun `run totals follow bytes when known and give the time left`() {
        val run = RunTotals(doneFiles = 3, totalFiles = 10, doneBytes = 250, totalBytes = 1_000, bytesPerSecond = 100)
        assertEquals(0.25f, run.fraction)
        assertEquals(8L, run.secondsLeft)
        assertEquals(0.3f, RunTotals(3, 10, 0, 0).fraction)
        assertNull(RunTotals(3, 10, 0, 0).secondsLeft)
        assertEquals(0f, RunTotals(0, 0, 0, 0).fraction)
        assertNull(RunTotals(10, 10, 1_000, 1_000, 100).secondsLeft)
    }
}
