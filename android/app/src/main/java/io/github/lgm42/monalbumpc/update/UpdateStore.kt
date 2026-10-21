package io.github.lgm42.monalbumpc.update

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.MutablePreferences
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.longPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import io.github.lgm42.monalbumpc.net.ApkInfo
import java.io.IOException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.updateDataStore: DataStore<Preferences> by preferencesDataStore(name = "update")

/** Self-update state: offered version, verified download, and what was already tried or notified. */
class UpdateStore(context: Context) {

    private val dataStore = context.applicationContext.updateDataStore

    val state: Flow<UpdateState> = dataStore.data
        .catch { e -> if (e is IOException) emit(emptyPreferences()) else throw e }
        .map { p ->
            UpdateState(
                availableCode = p[AVAILABLE_CODE] ?: 0,
                availableName = p[AVAILABLE_NAME].orEmpty(),
                availableSha256 = p[AVAILABLE_SHA].orEmpty(),
                availableSize = p[AVAILABLE_SIZE] ?: 0L,
                downloadedCode = p[DOWNLOADED_CODE] ?: 0,
                downloadedPath = p[DOWNLOADED_PATH],
                notifiedCode = p[NOTIFIED_CODE] ?: 0,
                autoInstallCode = p[AUTO_INSTALL_CODE] ?: 0,
                silentInstallCode = p[SILENT_INSTALL_CODE] ?: 0,
                rejectedSha256 = p[REJECTED_SHA],
            )
        }
        .distinctUntilChanged()

    suspend fun current(): UpdateState = state.first()

    suspend fun setAvailable(apk: ApkInfo) {
        val sha = apk.sha256.lowercase()
        dataStore.edit { p ->
            // Another APK than the one on disk: the file no longer counts (it is deleted by the next cleanup).
            if (p[AVAILABLE_CODE] != apk.versionCode || p[AVAILABLE_SHA] != sha) clearDownloaded(p)
            p[AVAILABLE_CODE] = apk.versionCode
            p[AVAILABLE_NAME] = apk.versionName
            p[AVAILABLE_SHA] = sha
            p[AVAILABLE_SIZE] = apk.size
        }
    }

    suspend fun clearAvailable() {
        dataStore.edit { p ->
            p.remove(AVAILABLE_CODE)
            p.remove(AVAILABLE_NAME)
            p.remove(AVAILABLE_SHA)
            p.remove(AVAILABLE_SIZE)
        }
    }

    suspend fun setDownloaded(versionCode: Int, path: String) {
        dataStore.edit { p ->
            p[DOWNLOADED_CODE] = versionCode
            p[DOWNLOADED_PATH] = path
        }
    }

    suspend fun clearDownloaded() {
        dataStore.edit { p -> clearDownloaded(p) }
    }

    suspend fun setRejected(sha256: String) {
        dataStore.edit { it[REJECTED_SHA] = sha256.lowercase() }
    }

    suspend fun clearRejected() {
        dataStore.edit { it.remove(REJECTED_SHA) }
    }

    suspend fun setNotified(versionCode: Int) {
        dataStore.edit { it[NOTIFIED_CODE] = versionCode }
    }

    suspend fun setAutoInstalled(versionCode: Int) {
        dataStore.edit { it[AUTO_INSTALL_CODE] = versionCode }
    }

    suspend fun setSilentInstalled(versionCode: Int) {
        dataStore.edit { it[SILENT_INSTALL_CODE] = versionCode }
    }

    private fun clearDownloaded(p: MutablePreferences) {
        p.remove(DOWNLOADED_CODE)
        p.remove(DOWNLOADED_PATH)
    }

    private companion object {
        val AVAILABLE_CODE = intPreferencesKey("available_code")
        val AVAILABLE_NAME = stringPreferencesKey("available_name")
        val AVAILABLE_SHA = stringPreferencesKey("available_sha256")
        val AVAILABLE_SIZE = longPreferencesKey("available_size")
        val DOWNLOADED_CODE = intPreferencesKey("downloaded_code")
        val DOWNLOADED_PATH = stringPreferencesKey("downloaded_path")
        val NOTIFIED_CODE = intPreferencesKey("notified_code")
        val AUTO_INSTALL_CODE = intPreferencesKey("auto_install_code")
        val SILENT_INSTALL_CODE = intPreferencesKey("silent_install_code")
        val REJECTED_SHA = stringPreferencesKey("rejected_sha256")
    }
}
