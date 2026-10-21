package io.github.lgm42.monalbumpc.update

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import io.github.lgm42.monalbumpc.MainActivity
import io.github.lgm42.monalbumpc.R

/** "Mise à jour disponible", posted from the background when the update needs the user. */
class UpdateNotifications(context: Context) {

    private val context = context.applicationContext

    fun ensureChannel() {
        val manager = context.getSystemService(NotificationManager::class.java) ?: return
        val channel = NotificationChannel(
            CHANNEL_UPDATES,
            context.getString(R.string.notif_channel_updates),
            NotificationManager.IMPORTANCE_DEFAULT,
        )
        manager.createNotificationChannel(channel)
    }

    /** Returns whether the notification could be posted. */
    fun notifyAvailable(versionName: String): Boolean {
        if (!canNotify()) return false
        ensureChannel()
        val text = context.getString(R.string.notif_update_text, versionName)
        val notification = NotificationCompat.Builder(context, CHANNEL_UPDATES)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(context.getString(R.string.notif_update_title))
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setContentIntent(openAppToInstall())
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()
        return try {
            NotificationManagerCompat.from(context).notify(ID_UPDATE, notification)
            true
        } catch (e: SecurityException) {
            false
        }
    }

    fun cancel() {
        NotificationManagerCompat.from(context).cancel(ID_UPDATE)
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

    private fun openAppToInstall(): PendingIntent {
        val intent = Intent(context, MainActivity::class.java)
            .putExtra(EXTRA_INSTALL_UPDATE, true)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        return PendingIntent.getActivity(
            context,
            REQUEST_INSTALL,
            intent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
    }

    companion object {
        const val CHANNEL_UPDATES = "updates"
        const val ID_UPDATE = 1004
        private const val REQUEST_INSTALL = 1

        /** On the intent opening [MainActivity]: install the downloaded update. */
        const val EXTRA_INSTALL_UPDATE = "io.github.lgm42.monalbumpc.INSTALL_UPDATE"
    }
}
