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

    private val texts = ContextTextSource(context)

    /**
     * Shown by the foreground worker, with both levels of progress and readable without expanding (see
     * [NotificationTexts]): "Sauvegarde · 4 / 20" over "IMG_1234.jpg · 45 Mo / 120 Mo", the bar of the file being sent
     * (a video takes minutes: the count alone would look stuck), the server in the header; expanded, the pace and the
     * whole run. Before sending, what the run is doing.
     */
    fun progressInfo(done: Int, total: Int, pcName: String, activity: RunActivity?, run: RunTotals? = null): ForegroundInfo {
        ensureChannels()
        val words = NotificationTexts.progress(texts, done, total, pcName, activity, run)
        val builder = NotificationCompat.Builder(context, CHANNEL_PROGRESS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(openApp())
            .setOnlyAlertOnce(true)
            .setOngoing(true)
            .setSilent(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_PROGRESS)
            .addAction(R.drawable.ic_notification, context.getString(R.string.notif_action_pause), pauseIntent())
            .setWords(words)
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

    /** "Tout est sauvegardé" over "52 photos et 3 vidéos envoyées"; [pending] and [failed] are what is left. */
    fun notifyDone(photos: Int, videos: Int, pending: Int, failed: Int, pcName: String) {
        if (photos + videos <= 0) return
        val notification = NotificationCompat.Builder(context, CHANNEL_PROGRESS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(openApp())
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setWords(NotificationTexts.done(texts, photos, videos, pending, failed, pcName))
            .build()
        notify(ID_DONE, notification)
    }

    /** "Disque de NAS plein" over "Les photos attendent de la place". */
    fun notifyIssue(issue: BackupIssue, pcName: String) {
        val words = NotificationTexts.issue(texts, issue, pcName) ?: return
        val notification = NotificationCompat.Builder(context, CHANNEL_ALERTS)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentIntent(openApp())
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setWords(words)
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

    /** "Mettre en pause": handled by [BackupActionReceiver], without opening the app. */
    private fun pauseIntent(): PendingIntent {
        val intent = Intent(context, BackupActionReceiver::class.java).setAction(BackupActionReceiver.ACTION_PAUSE)
        return PendingIntent.getBroadcast(context, 0, intent, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
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

/** Title and text read collapsed, the details when expanded, the server in the header. */
fun NotificationCompat.Builder.setWords(words: NotificationText): NotificationCompat.Builder {
    setContentTitle(words.title)
    words.text?.let { setContentText(it) }
    words.subText?.let { setSubText(it) }
    words.expanded?.let { setStyle(NotificationCompat.BigTextStyle().bigText(it)) }
    return this
}
