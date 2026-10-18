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

    /** "Sauvegarde en cours · 12 / 52 · PC-SALON", shown by the foreground worker. */
    fun progressInfo(done: Int, total: Int, pcName: String): ForegroundInfo {
        ensureChannels()
        val text = if (total > 0) {
            context.getString(R.string.notif_progress, done, total, pcName)
        } else {
            context.getString(R.string.notif_progress_preparing, pcName)
        }
        val notification = NotificationCompat.Builder(context, CHANNEL_PROGRESS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(text)
            .setContentIntent(openApp())
            .setOnlyAlertOnce(true)
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_PROGRESS)
            .setProgress(total.coerceAtLeast(0), done.coerceIn(0, total.coerceAtLeast(0)), total <= 0)
            .build()
        return foregroundInfo(notification)
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
    }
}
