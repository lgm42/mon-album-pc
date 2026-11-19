package io.github.lgm42.monalbumpc.backup

import android.Manifest
import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.content.pm.ServiceInfo
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import androidx.work.ForegroundInfo
import io.github.lgm42.monalbumpc.MainActivity
import io.github.lgm42.monalbumpc.R

/** The only notifications of the app (design README §6). Nothing stays at rest. */
class BackupNotifications(context: Context) {

    private val context = context.applicationContext

    fun ensureChannels() {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        val progress = NotificationChannel(
            CHANNEL_PROGRESS,
            context.getString(R.string.notif_channel_backup),
            NotificationManager.IMPORTANCE_LOW,
        ).apply { setShowBadge(false) }
        val alerts = NotificationChannel(
            CHANNEL_ALERTS,
            context.getString(R.string.notif_channel_alerts),
            NotificationManager.IMPORTANCE_DEFAULT,
        )
        manager.createNotificationChannel(progress)
        manager.createNotificationChannel(alerts)
    }

    /**
     * Shown by the foreground worker, with both levels of progress: "Envoi vers NAS · VID-….mp4" over "4 / 20 · 45 Mo /
     * 120 Mo · 2,1 Mo/s" and the bar of the file being sent (a video takes minutes: the count alone would look stuck);
     * expanded, the whole run ("Total : 12 / 52 fichiers · 340 Mo / 1,2 Go"). Before sending, what the run is doing.
     */
    fun progressInfo(done: Int, total: Int, pcName: String, activity: RunActivity?, run: RunTotals? = null): ForegroundInfo {
        ensureChannels()
        val position = if (total > 0) (done + 1).coerceAtMost(total) else 0
        val uploading = activity?.phase == BackupPhase.UPLOADING && total > 0
        val title = when {
            uploading && activity?.fileName != null -> context.getString(R.string.notif_sending_file, pcName, activity.fileName)
            uploading -> context.getString(R.string.notif_sending, position, total, pcName)
            activity?.phase == BackupPhase.PREPARING && total > 0 ->
                context.getString(R.string.notif_preparing_files, position, total, pcName)
            activity != null -> TransferText.title(context, done, total, activity, pcName)
            total > 0 -> context.getString(R.string.notif_progress, done, total, pcName)
            else -> context.getString(R.string.notif_progress_preparing, pcName)
        }
        val text = when {
            uploading && activity != null -> listOfNotNull(
                context.getString(R.string.notif_position, position, total),
                TransferText.bytes(context, activity),
            ).joinToString(" · ")
            else -> activity?.fileName
        }
        val expanded = listOfNotNull(
            text,
            run?.takeIf { uploading }?.let { totals ->
                context.getString(
                    R.string.notif_total,
                    listOfNotNull(TransferText.run(context, totals), TransferText.runLeft(context, totals)).joinToString(" · "),
                )
            },
        ).joinToString("\n").takeIf { it.isNotEmpty() }
        val builder = NotificationCompat.Builder(context, CHANNEL_PROGRESS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentIntent(openApp())
            .setOnlyAlertOnce(true)
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_PROGRESS)
        if (text != null) builder.setContentText(text)
        if (expanded != null) builder.setStyle(NotificationCompat.BigTextStyle().bigText(expanded))
        val fileFraction = activity?.fileFraction
        if (fileFraction != null) {
            builder.setProgress(PROGRESS_MAX, (fileFraction * PROGRESS_MAX).toInt(), false)
        } else {
            builder.setProgress(total.coerceAtLeast(0), done.coerceIn(0, total.coerceAtLeast(0)), total <= 0)
        }
        return foregroundInfo(builder.build())
    }

    private fun foregroundInfo(notification: Notification): ForegroundInfo =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            ForegroundInfo(ID_PROGRESS, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC)
        } else {
            ForegroundInfo(ID_PROGRESS, notification)
        }

    /** "52 photos et 3 vidéos sauvegardées sur PC-SALON". */
    fun notifyDone(photos: Int, videos: Int, pcName: String) {
        if (photos + videos <= 0) return
        val res = context.resources
        val parts = buildList {
            if (photos > 0) add(res.getQuantityString(R.plurals.notif_photos, photos, photos))
            if (videos > 0) add(res.getQuantityString(R.plurals.notif_videos, videos, videos))
        }
        val what = parts.joinToString(context.getString(R.string.notif_and))
        val text = res.getQuantityString(R.plurals.notif_done, photos + videos, what, pcName)
        val notification = NotificationCompat.Builder(context, CHANNEL_PROGRESS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(text)
            .setContentIntent(openApp())
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()
        notify(ID_DONE, notification)
    }

    /** "Sauvegarde impossible : le disque du PC est plein." */
    fun notifyIssue(issue: BackupIssue, pcName: String) {
        val text = when (issue) {
            BackupIssue.DISK_FULL -> context.getString(R.string.notif_error_disk_full)
            BackupIssue.DESTINATION_UNAVAILABLE -> context.getString(R.string.notif_error_destination, pcName)
            else -> return
        }
        val notification = NotificationCompat.Builder(context, CHANNEL_ALERTS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(openApp())
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()
        notify(ID_ERROR, notification)
    }

    fun cancelIssue() {
        NotificationManagerCompat.from(context).cancel(ID_ERROR)
    }

    private fun notify(id: Int, notification: Notification) {
        if (!canNotify()) return
        ensureChannels()
        try {
            NotificationManagerCompat.from(context).notify(id, notification)
        } catch (e: SecurityException) {
            // Permission revoked in the meantime.
        }
    }

    private fun canNotify(): Boolean {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            return false
        }
        return NotificationManagerCompat.from(context).areNotificationsEnabled()
    }

    private fun openApp(): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        return PendingIntent.getActivity(
            context,
            0,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    companion object {
        const val CHANNEL_PROGRESS = "backup"
        const val CHANNEL_ALERTS = "backup_alerts"
        const val ID_PROGRESS = 1001
        const val ID_DONE = 1002
        const val ID_ERROR = 1003
        private const val PROGRESS_MAX = 1000
    }
}
