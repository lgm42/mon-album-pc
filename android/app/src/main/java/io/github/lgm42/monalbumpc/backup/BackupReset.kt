package io.github.lgm42.monalbumpc.backup

import android.content.Context
import io.github.lgm42.monalbumpc.data.BackupPrefs
import io.github.lgm42.monalbumpc.data.PairingStore
import io.github.lgm42.monalbumpc.data.media.AppDatabase
import io.github.lgm42.monalbumpc.media.FolderStore

/** Forgets everything tied to the paired PC (unpairing, or `device_unknown` from the PC). */
object BackupReset {

    suspend fun forgetPc(context: Context) {
        val appContext = context.applicationContext
        AppDatabase.get(appContext).mediaDao().deleteAll()
        FolderStore(appContext).clear()
        BackupPrefs(appContext).clearRun()
        BackupNotifications(appContext).cancelIssue()
        // The app switches to the onboarding as soon as the pairing disappears.
        PairingStore(appContext).clear()
        // Last, since it may cancel the worker calling this.
        BackupScheduler.cancelAll(appContext)
    }
}
