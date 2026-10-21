package io.github.lgm42.monalbumpc.update

import android.content.Context
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import io.github.lgm42.monalbumpc.BuildConfig
import io.github.lgm42.monalbumpc.backup.BackupWorker
import io.github.lgm42.monalbumpc.data.BackupPrefs
import io.github.lgm42.monalbumpc.data.PairingStore
import io.github.lgm42.monalbumpc.net.ApiException
import io.github.lgm42.monalbumpc.net.PcApi
import java.io.File
import java.io.IOException
import java.util.concurrent.TimeUnit
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withContext

/**
 * Downloads the APK offered by the PC (protocol §5.5) into `filesDir/updates`, checks its size and
 * SHA-256, keeps only that file, then tries a silent install if the app is not shown.
 */
class UpdateWorker(appContext: Context, params: WorkerParameters) : CoroutineWorker(appContext, params) {

    override suspend fun doWork(): Result {
        val context = applicationContext
        val store = UpdateStore(context)
        val installed = BuildConfig.VERSION_CODE
        UpdateManager.cleanup(context)
        val state = store.current()
        if (!UpdateLogic.needsDownload(state, installed)) return Result.success()
        val apk = state.available ?: return Result.success()
        val pc = PairingStore(context).paired.first() ?: return Result.success()

        val dir = UpdateManager.updatesDir(context)
        val target = File(dir, UpdateLogic.fileName(apk.versionCode))
        val part = File(dir, target.name + PART_SUFFIX)
        UpdateManager.setDownloadProgress(0f)
        try {
            withContext(Dispatchers.IO) {
                if (!dir.isDirectory && !dir.mkdirs()) throw IOException("Cannot create the updates folder")
                part.delete()
            }
            PcApi(pc.host, pc.apiPort, pc.certSha256).downloadApk(pc.authorization, part, apk.size) { bytes ->
                UpdateManager.setDownloadProgress((bytes.toFloat() / apk.size).coerceIn(0f, 1f))
            }
            val verified = withContext(Dispatchers.IO) {
                part.inputStream().use { UpdateLogic.verify(it, apk.size, apk.sha256) }
            }
            if (!verified) {
                // Not what the PC announced: never downloaded again, unless the PC offers another APK.
                part.delete()
                store.setRejected(apk.sha256)
                UpdateManager.setDownloadFailed(true)
                return Result.failure()
            }
            withContext(Dispatchers.IO) {
                // Only the latest file is kept.
                dir.listFiles()?.forEach { if (it != part) it.delete() }
                if (!part.renameTo(target)) throw IOException("Cannot rename the downloaded APK")
            }
            store.setDownloaded(apk.versionCode, target.path)
            UpdateManager.setDownloadFailed(false)
        } catch (e: CancellationException) {
            part.delete()
            throw e
        } catch (e: Exception) {
            // ApiException (PC unreachable, older server…) or I/O: try again a few times, then wait for the next check.
            part.delete()
            if (e is ApiException && runAttemptCount < MAX_ATTEMPTS) return Result.retry()
            UpdateManager.setDownloadFailed(true)
            return Result.failure()
        } finally {
            UpdateManager.setDownloadProgress(null)
        }

        // Shown: the app's card installs it. Hidden: try now, unless a backup is transferring.
        UpdateManager.afterBackgroundRun(context, backupRunning = BackupWorker.running.value)
        return Result.success()
    }

    companion object {
        private const val UNIQUE_NAME = "update-download"
        private const val PART_SUFFIX = ".part"
        private const val MAX_ATTEMPTS = 3

        /** Same network rule as the backups: Wi-Fi only unless mobile data is allowed. */
        suspend fun enqueue(context: Context) {
            val mobileData = BackupPrefs(context).current().mobileData
            val request = OneTimeWorkRequestBuilder<UpdateWorker>()
                .setConstraints(
                    Constraints.Builder()
                        .setRequiredNetworkType(if (mobileData) NetworkType.CONNECTED else NetworkType.UNMETERED)
                        .build(),
                )
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 1, TimeUnit.MINUTES)
                .build()
            WorkManager.getInstance(context).enqueueUniqueWork(UNIQUE_NAME, ExistingWorkPolicy.KEEP, request)
        }
    }
}
