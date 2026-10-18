package io.github.lgm42.monalbumpc.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import io.github.lgm42.monalbumpc.backup.BackupIssue
import java.io.IOException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.backupDataStore: DataStore<Preferences> by preferencesDataStore(name = "backup")

/** Backup settings and the outcome of the last run. */
data class BackupState(
    val chargingOnly: Boolean = false,
    val lastBackupMs: Long? = null,
    val issue: BackupIssue = BackupIssue.NONE,
    val issueSinceMs: Long? = null,
    val notifiedIssue: BackupIssue = BackupIssue.NONE,
)

class BackupPrefs(context: Context) {

    private val dataStore = context.applicationContext.backupDataStore

    val state: Flow<BackupState> = dataStore.data
        .catch { e -> if (e is IOException) emit(emptyPreferences()) else throw e }
        .map { p ->
            BackupState(
                chargingOnly = p[CHARGING_ONLY] ?: false,
                lastBackupMs = p[LAST_BACKUP],
                issue = parseIssue(p[ISSUE]),
                issueSinceMs = p[ISSUE_SINCE],
                notifiedIssue = parseIssue(p[NOTIFIED_ISSUE]),
            )
        }
        .distinctUntilChanged()

    suspend fun current(): BackupState = state.first()

    suspend fun setChargingOnly(value: Boolean) {
        dataStore.edit { it[CHARGING_ONLY] = value }
    }

    suspend fun setLastBackup(timeMs: Long) {
        dataStore.edit { it[LAST_BACKUP] = timeMs }
    }

    /** Records the issue; its start time is kept while the same issue lasts. */
    suspend fun setIssue(issue: BackupIssue, nowMs: Long) {
        dataStore.edit { p ->
            val previous = parseIssue(p[ISSUE])
            p[ISSUE] = issue.name
            if (issue == BackupIssue.NONE) {
                p.remove(ISSUE_SINCE)
                p[NOTIFIED_ISSUE] = BackupIssue.NONE.name
            } else if (previous != issue || p[ISSUE_SINCE] == null) {
                p[ISSUE_SINCE] = nowMs
            }
        }
    }

    suspend fun setNotifiedIssue(issue: BackupIssue) {
        dataStore.edit { it[NOTIFIED_ISSUE] = issue.name }
    }

    /** After unpairing; the charging setting is kept. */
    suspend fun clearRun() {
        dataStore.edit { p ->
            p.remove(LAST_BACKUP)
            p.remove(ISSUE)
            p.remove(ISSUE_SINCE)
            p.remove(NOTIFIED_ISSUE)
        }
    }

    private fun parseIssue(value: String?): BackupIssue =
        BackupIssue.entries.firstOrNull { it.name == value } ?: BackupIssue.NONE

    private companion object {
        val CHARGING_ONLY = booleanPreferencesKey("charging_only")
        val LAST_BACKUP = longPreferencesKey("last_backup")
        val ISSUE = stringPreferencesKey("issue")
        val ISSUE_SINCE = longPreferencesKey("issue_since")
        val NOTIFIED_ISSUE = stringPreferencesKey("notified_issue")
    }
}
