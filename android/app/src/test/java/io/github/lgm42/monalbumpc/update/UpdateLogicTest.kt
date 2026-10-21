package io.github.lgm42.monalbumpc.update

import io.github.lgm42.monalbumpc.net.ApkInfo
import io.github.lgm42.monalbumpc.net.PcApi
import io.github.lgm42.monalbumpc.net.StatusResponse
import java.io.ByteArrayInputStream
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class UpdateLogicTest {

    // SHA-256 of "abc".
    private val abcSha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
    private val apk8 = ApkInfo(versionCode = 8, versionName = "0.1.8", sha256 = abcSha, size = 3)

    private fun downloaded(code: Int = 8, auto: Int = 0, silent: Int = 0, notified: Int = 0) = UpdateState(
        availableCode = code,
        availableName = "0.1.$code",
        availableSha256 = abcSha,
        availableSize = 3,
        downloadedCode = code,
        downloadedPath = "/data/updates/monalbumpc-$code.apk",
        autoInstallCode = auto,
        silentInstallCode = silent,
        notifiedCode = notified,
    )

    @Test
    fun `status parses the apk offer, or none`() {
        val withApk = PcApi.json.decodeFromString(
            StatusResponse.serializer(),
            """{"protocol":1,"pcName":"PC","apk":{"versionCode":8,"versionName":"0.1.8","sha256":"$abcSha","size":3},"extra":1}""",
        )
        assertEquals(apk8, withApk.apk)
        val without = PcApi.json.decodeFromString(StatusResponse.serializer(), """{"protocol":1,"apk":null}""")
        assertNull(without.apk)
    }

    @Test
    fun `only a strictly newer and well described apk is an update`() {
        assertTrue(UpdateLogic.isNewer(apk8, 7))
        assertFalse(UpdateLogic.isNewer(apk8, 8))
        assertFalse(UpdateLogic.isNewer(apk8, 9))
        assertFalse(UpdateLogic.isNewer(null, 1))
        assertFalse(UpdateLogic.isNewer(apk8.copy(sha256 = "…"), 7))
        assertFalse(UpdateLogic.isNewer(apk8.copy(size = 0), 7))
        assertFalse(UpdateLogic.isNewer(apk8.copy(size = UpdateLogic.MAX_APK_SIZE + 1), 7))
        assertTrue(UpdateLogic.isNewer(apk8.copy(sha256 = abcSha.uppercase()), 7))
    }

    @Test
    fun `download is needed once, and never again for a rejected apk`() {
        val offered = UpdateState(availableCode = 8, availableName = "0.1.8", availableSha256 = abcSha, availableSize = 3)
        assertTrue(UpdateLogic.needsDownload(offered, 7))
        assertFalse(UpdateLogic.needsDownload(offered, 8))
        assertFalse(UpdateLogic.needsDownload(downloaded(), 7))
        assertFalse(UpdateLogic.needsDownload(offered.copy(rejectedSha256 = abcSha), 7))
        assertTrue(UpdateLogic.needsDownload(offered.copy(rejectedSha256 = "0".repeat(64)), 7))
        assertFalse(UpdateLogic.needsDownload(UpdateState(), 7))
        // A file of an older offer does not count for the new one.
        assertTrue(UpdateLogic.needsDownload(downloaded(8).copy(availableCode = 9), 7))
    }

    @Test
    fun `a downloaded file becomes obsolete once the app reaches its version`() {
        assertFalse(UpdateLogic.isObsolete(8, 7))
        assertTrue(UpdateLogic.isObsolete(8, 8))
        assertTrue(UpdateLogic.isObsolete(8, 9))
        assertFalse(UpdateLogic.isObsolete(0, 9))
        assertEquals("monalbumpc-8.apk", UpdateLogic.fileName(8))
    }

    @Test
    fun `sha256 verification checks both size and hash`() {
        val bytes = "abc".toByteArray()
        assertTrue(UpdateLogic.verify(ByteArrayInputStream(bytes), 3, abcSha))
        assertTrue(UpdateLogic.verify(ByteArrayInputStream(bytes), 3, abcSha.uppercase()))
        assertFalse(UpdateLogic.verify(ByteArrayInputStream(bytes), 4, abcSha))
        assertFalse(UpdateLogic.verify(ByteArrayInputStream("abd".toByteArray()), 3, abcSha))
        assertFalse(UpdateLogic.verify(ByteArrayInputStream(bytes), 3, ""))
        assertTrue(UpdateLogic.sha256Matches(abcSha, abcSha.uppercase()))
        assertFalse(UpdateLogic.sha256Matches("abc", "abc"))
    }

    @Test
    fun `auto install runs once per version, when allowed and no backup runs`() {
        assertTrue(UpdateLogic.shouldAutoInstall(downloaded(), 7, canInstallPackages = true, backupRunning = false))
        assertFalse(UpdateLogic.shouldAutoInstall(downloaded(auto = 8), 7, true, false))
        assertTrue(UpdateLogic.shouldAutoInstall(downloaded(auto = 7), 7, true, false))
        assertFalse(UpdateLogic.shouldAutoInstall(downloaded(), 7, canInstallPackages = false, backupRunning = false))
        assertFalse(UpdateLogic.shouldAutoInstall(downloaded(), 7, canInstallPackages = true, backupRunning = true))
        assertFalse(UpdateLogic.shouldAutoInstall(downloaded(), 8, true, false))
        assertFalse(UpdateLogic.shouldAutoInstall(downloaded().copy(downloadedPath = null), 7, true, false))
    }

    @Test
    fun `background decision table`() {
        fun action(state: UpdateState, sdk: Int = 34, fg: Boolean = false, running: Boolean = false, installed: Int = 7) =
            UpdateLogic.backgroundAction(state, installed, sdk, fg, running)

        // Android 12+: silent install first, once per version, then a single notification.
        assertEquals(BackgroundAction.SILENT_INSTALL, action(downloaded()))
        assertEquals(BackgroundAction.NOTIFY, action(downloaded(silent = 8)))
        assertEquals(BackgroundAction.NONE, action(downloaded(silent = 8, notified = 8)))
        // Android 10-11: no silent install, notify once.
        assertEquals(BackgroundAction.NOTIFY, action(downloaded(), sdk = 30))
        assertEquals(BackgroundAction.NONE, action(downloaded(notified = 8), sdk = 30))
        // A newer version is tried and notified again.
        assertEquals(BackgroundAction.SILENT_INSTALL, action(downloaded(9, silent = 8, notified = 8)))
        // Never while shown, during a backup, before the download, or when already up to date.
        assertEquals(BackgroundAction.NONE, action(downloaded(), fg = true))
        assertEquals(BackgroundAction.NONE, action(downloaded(), running = true))
        assertEquals(BackgroundAction.NONE, action(downloaded().copy(downloadedCode = 0, downloadedPath = null)))
        assertEquals(BackgroundAction.NONE, action(downloaded(), installed = 8))
    }

    @Test
    fun `notification once per version`() {
        assertTrue(UpdateLogic.shouldNotify(UpdateState(), 8))
        assertFalse(UpdateLogic.shouldNotify(UpdateState(notifiedCode = 8), 8))
        assertTrue(UpdateLogic.shouldNotify(UpdateState(notifiedCode = 8), 9))
        assertFalse(UpdateLogic.shouldNotify(UpdateState(), 0))
    }

    @Test
    fun `state exposes the offer and whether it is on disk`() {
        assertNull(UpdateState().available)
        assertNotNull(downloaded().available)
        assertTrue(downloaded().isDownloaded)
        assertFalse(downloaded().copy(downloadedCode = 7).isDownloaded)
    }
}
