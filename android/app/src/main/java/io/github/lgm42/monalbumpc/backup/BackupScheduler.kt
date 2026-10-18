package io.github.lgm42.monalbumpc.backup

import android.content.Context
import android.provider.MediaStore
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.OutOfQuotaPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import java.util.concurrent.TimeUnit

/**
 * Schedules [BackupWorker] (cdc §4.2): when a new photo or video appears, every hour as a
 * fallback, and on demand. Wi-Fi only (unmetered), optionally only while charging.
 */
object BackupScheduler {

    const val TAG = "backup"
    const val PERIODIC = "backup-periodic"
    const val CONTENT = "backup-content"
    const val NOW = "backup-now"

    /** Tag of the work woken up by new media, which re-arms itself after each run. */
    const val TAG_CONTENT = "backup-content-trigger"

    /** Every unique work name, newest-first priority for the UI. */
    val uniqueNames = listOf(NOW, CONTENT, PERIODIC)

    /** On app start and after pairing; [replace] after the charging setting changed. */
    fun schedule(context: Context, chargingOnly: Boolean, replace: Boolean = false) {
        val workManager = WorkManager.getInstance(context)
        val periodic = PeriodicWorkRequestBuilder<BackupWorker>(1, TimeUnit.HOURS)
            .setConstraints(constraints(chargingOnly))
            .addTag(TAG)
            .build()
        workManager.enqueueUniquePeriodicWork(
            PERIODIC,
            if (replace) ExistingPeriodicWorkPolicy.UPDATE else ExistingPeriodicWorkPolicy.KEEP,
            periodic,
        )
        armContentTrigger(context, chargingOnly, if (replace) ExistingWorkPolicy.REPLACE else ExistingWorkPolicy.KEEP)
    }

    /** One-time work that waits for a change in the photo or video collections. */
    fun armContentTrigger(context: Context, chargingOnly: Boolean, policy: ExistingWorkPolicy) {
        val constraints = Constraints.Builder()
            .setRequiredNetworkType(NetworkType.UNMETERED)
            .setRequiresCharging(chargingOnly)
            .addContentUriTrigger(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, true)
            .addContentUriTrigger(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, true)
            .setTriggerContentUpdateDelay(10, TimeUnit.SECONDS)
            .setTriggerContentMaxDelay(2, TimeUnit.MINUTES)
            .build()
        val request = OneTimeWorkRequestBuilder<BackupWorker>()
            .setConstraints(constraints)
            .addTag(TAG)
            .addTag(TAG_CONTENT)
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(CONTENT, policy, request)
    }

    /** "Sauvegarder maintenant": expedited, ignores the charging setting, still Wi-Fi only. */
    fun backupNow(context: Context) {
        val request = OneTimeWorkRequestBuilder<BackupWorker>()
            .setExpedited(OutOfQuotaPolicy.RUN_AS_NON_EXPEDITED_WORK_REQUEST)
            .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.UNMETERED).build())
            .addTag(TAG)
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(NOW, ExistingWorkPolicy.KEEP, request)
    }

    fun cancelAll(context: Context) {
        val workManager = WorkManager.getInstance(context)
        uniqueNames.forEach { workManager.cancelUniqueWork(it) }
        workManager.cancelAllWorkByTag(TAG)
    }

    private fun constraints(chargingOnly: Boolean): Constraints = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.UNMETERED)
        .setRequiresCharging(chargingOnly)
        .build()
}
