package io.github.lgm42.monalbumpc.update

import android.annotation.SuppressLint
import android.content.ActivityNotFoundException
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import androidx.core.content.IntentCompat

/** Outcome of a [UpdateInstaller] session (not exported: only the system's PendingIntent reaches it). */
class UpdateResultReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
        val versionCode = intent.getIntExtra(UpdateInstaller.EXTRA_VERSION_CODE, 0)
        val versionName = intent.getStringExtra(UpdateInstaller.EXTRA_VERSION_NAME).orEmpty()
        when (status) {
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                val confirm = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_INTENT, Intent::class.java)
                if (confirm != null && UpdateManager.isForeground() && startConfirmation(context, confirm)) {
                    UpdateManager.onInstallWaitingForUser()
                } else {
                    // Never start an activity from the background: tell the user instead.
                    UpdateManager.onInstallNeedsUser(context, versionCode, versionName, goAsync())
                }
            }
            PackageInstaller.STATUS_SUCCESS -> UpdateManager.onInstallFinished()
            else -> UpdateManager.onInstallFailed(status)
        }
    }

    // The intent comes from the system installer (PackageInstaller.EXTRA_INTENT) through our own
    // explicit, non-exported PendingIntent: this is the documented confirmation flow.
    @SuppressLint("UnsafeIntentLaunch")
    private fun startConfirmation(context: Context, confirm: Intent): Boolean = try {
        context.startActivity(confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        true
    } catch (e: ActivityNotFoundException) {
        false
    } catch (e: SecurityException) {
        false
    }
}
