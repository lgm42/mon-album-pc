package io.github.lgm42.monalbumpc.update

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Installs a verified APK through a [PackageInstaller] session. The outcome reaches
 * [UpdateResultReceiver]; on success the system replaces this process.
 */
object UpdateInstaller {

    const val EXTRA_VERSION_CODE = "io.github.lgm42.monalbumpc.UPDATE_VERSION_CODE"
    const val EXTRA_VERSION_NAME = "io.github.lgm42.monalbumpc.UPDATE_VERSION_NAME"

    /** Writes [file] into a new session and commits it. Throws on I/O or session errors. */
    suspend fun install(context: Context, file: File, versionCode: Int, versionName: String) = withContext(Dispatchers.IO) {
        val appContext = context.applicationContext
        val installer = appContext.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(appContext.packageName)
            setSize(file.length())
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                // Silent once this app is the installer of record of itself; the system asks otherwise.
                setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
            }
        }
        val sessionId = installer.createSession(params)
        val session = installer.openSession(sessionId)
        try {
            session.openWrite(APK_NAME, 0, file.length()).use { output ->
                file.inputStream().use { input -> input.copyTo(output, BUFFER_SIZE) }
                session.fsync(output)
            }
            val intent = Intent(appContext, UpdateResultReceiver::class.java)
                .setPackage(appContext.packageName)
                .putExtra(EXTRA_VERSION_CODE, versionCode)
                .putExtra(EXTRA_VERSION_NAME, versionName)
            // The system adds the status extras to this intent: it must be mutable (explicit, so safe).
            val flags = PendingIntent.FLAG_UPDATE_CURRENT or
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) PendingIntent.FLAG_MUTABLE else 0
            val pending = PendingIntent.getBroadcast(appContext, sessionId, intent, flags)
            session.commit(pending.intentSender)
            session.close()
        } catch (e: Exception) {
            session.abandon()
            throw e
        }
    }

    private const val APK_NAME = "monalbumpc.apk"
    private const val BUFFER_SIZE = 256 * 1024
}
