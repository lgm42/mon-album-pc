package io.github.lgm42.monalbumpc.backup

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import io.github.lgm42.monalbumpc.data.BackupPrefs
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * "Mettre en pause" / "Reprendre". The pause is persisted: it stops the running work at once (its upload is cancelled
 * and resumed later from the server's offset) and removes every scheduled run, so that nothing starts again by itself;
 * a run that still slips in (a folder just ticked, a retry) sees the pause and stops before reading anything.
 */
object BackupControl {

    suspend fun pause(context: Context) {
        val appContext = context.applicationContext
        BackupPrefs(appContext).setPaused(true)
        BackupScheduler.cancelAll(appContext)
    }

    /** Schedules the automatic runs again and starts one now. */
    suspend fun resume(context: Context) {
        val appContext = context.applicationContext
        val prefs = BackupPrefs(appContext)
        prefs.setPaused(false)
        val conditions = prefs.current().conditions
        BackupScheduler.schedule(appContext, conditions)
        BackupScheduler.backupNow(appContext, conditions)
    }
}

/** The "Mettre en pause" button of the progress notification. */
class BackupActionReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != ACTION_PAUSE) return
        val pending = goAsync()
        scope.launch {
            try {
                BackupControl.pause(context)
            } finally {
                pending.finish()
            }
        }
    }

    companion object {
        const val ACTION_PAUSE = "io.github.lgm42.monalbumpc.action.PAUSE_BACKUP"

        private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    }
}
