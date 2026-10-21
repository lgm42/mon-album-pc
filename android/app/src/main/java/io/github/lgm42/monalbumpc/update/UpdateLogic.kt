package io.github.lgm42.monalbumpc.update

import io.github.lgm42.monalbumpc.backup.Sha256
import io.github.lgm42.monalbumpc.net.ApkInfo
import java.io.InputStream

/** What a background run does with a downloaded update. */
enum class BackgroundAction { NONE, SILENT_INSTALL, NOTIFY }

/** Persisted state of the self-update (see [UpdateStore]). */
data class UpdateState(
    /** APK offered by the PC, newer than the app when it was recorded; 0 when none. */
    val availableCode: Int = 0,
    val availableName: String = "",
    val availableSha256: String = "",
    val availableSize: Long = 0,
    /** Verified APK on disk, and its version. */
    val downloadedCode: Int = 0,
    val downloadedPath: String? = null,
    /** Last version for which the "update available" notification was posted. */
    val notifiedCode: Int = 0,
    /** Last version installed automatically while the app was in the foreground. */
    val autoInstallCode: Int = 0,
    /** Last version tried silently from the background. */
    val silentInstallCode: Int = 0,
    /** SHA-256 of an APK whose download did not match: not downloaded again. */
    val rejectedSha256: String? = null,
) {
    val available: ApkInfo?
        get() = if (availableCode > 0) ApkInfo(availableCode, availableName, availableSha256, availableSize) else null

    /** The offered update is on disk and verified. */
    val isDownloaded: Boolean
        get() = availableCode > 0 && downloadedCode == availableCode && downloadedPath != null
}

/** Pure decisions of the self-update, kept apart for unit tests. */
object UpdateLogic {

    private val SHA256_HEX = Regex("^[0-9a-fA-F]{64}$")

    /** Upper bound of an APK served by the PC, against a corrupt `size`. */
    const val MAX_APK_SIZE = 512L * 1024 * 1024

    /** The PC describes an APK the app can download and check. */
    fun isUsable(apk: ApkInfo?): Boolean =
        apk != null && apk.versionCode > 0 && apk.size in 1..MAX_APK_SIZE && SHA256_HEX.matches(apk.sha256)

    /** An update exists only for a strictly newer version: an older or equal APK never loops. */
    fun isNewer(apk: ApkInfo?, installedCode: Int): Boolean = isUsable(apk) && apk!!.versionCode > installedCode

    /** The offered version still has to be downloaded. */
    fun needsDownload(state: UpdateState, installedCode: Int): Boolean {
        val apk = state.available ?: return false
        if (!isNewer(apk, installedCode)) return false
        if (state.isDownloaded) return false
        return !apk.sha256.equals(state.rejectedSha256, ignoreCase = true)
    }

    /** A downloaded file is useless once the app is at least at its version. */
    fun isObsolete(downloadedCode: Int, installedCode: Int): Boolean = downloadedCode in 1..installedCode

    fun fileName(versionCode: Int): String = "monalbumpc-$versionCode.apk"

    fun sha256Matches(expected: String, actual: String): Boolean =
        SHA256_HEX.matches(expected) && expected.equals(actual, ignoreCase = true)

    /** Whether [input] has exactly [expectedSize] bytes with the SHA-256 [expectedSha256]. */
    fun verify(input: InputStream, expectedSize: Long, expectedSha256: String): Boolean {
        var size = 0L
        val sha = Sha256.hex(input) { size = it }
        return size == expectedSize && sha256Matches(expectedSha256, sha)
    }

    /** In the foreground, the downloaded update installs by itself once per version. */
    fun shouldAutoInstall(
        state: UpdateState,
        installedCode: Int,
        canInstallPackages: Boolean,
        backupRunning: Boolean,
    ): Boolean =
        state.isDownloaded &&
            state.availableCode > installedCode &&
            state.autoInstallCode != state.availableCode &&
            canInstallPackages &&
            !backupRunning

    /**
     * At the end of a background run: try a silent install once per version (Android 12+), otherwise
     * notify once per version. Never while the app is shown (the card handles it) or a backup runs.
     */
    fun backgroundAction(
        state: UpdateState,
        installedCode: Int,
        sdkInt: Int,
        inForeground: Boolean,
        backupRunning: Boolean,
    ): BackgroundAction {
        if (inForeground || backupRunning) return BackgroundAction.NONE
        if (!state.isDownloaded || state.availableCode <= installedCode) return BackgroundAction.NONE
        val code = state.availableCode
        if (sdkInt >= SDK_SILENT_INSTALL && state.silentInstallCode != code) return BackgroundAction.SILENT_INSTALL
        return if (shouldNotify(state, code)) BackgroundAction.NOTIFY else BackgroundAction.NONE
    }

    /** The "update available" notification is posted once per version. */
    fun shouldNotify(state: UpdateState, versionCode: Int): Boolean = versionCode > 0 && state.notifiedCode != versionCode

    /** Android 12 (S): `setRequireUserAction(USER_ACTION_NOT_REQUIRED)`. */
    const val SDK_SILENT_INSTALL = 31
}
