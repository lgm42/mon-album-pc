package io.github.lgm42.monalbumpc.media

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.stringSetPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import java.io.IOException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.map

private val Context.foldersDataStore: DataStore<Preferences> by preferencesDataStore(name = "folders")

/** Tracked folders, by key "volume|relative/path/" (see [FolderRules.key]). */
class FolderStore(context: Context) {

    private val dataStore = context.applicationContext.foldersDataStore

    private val prefs: Flow<Preferences> = dataStore.data
        .catch { e -> if (e is IOException) emit(emptyPreferences()) else throw e }

    /** Null until the default selection has been applied. */
    val tracked: Flow<Set<String>?> = prefs
        .map { p -> if (p[DEFAULTS_APPLIED] == true) p[TRACKED].orEmpty() else null }
        .distinctUntilChanged()

    /**
     * Returns the tracked folders, applying the default selection (recommended folders) the
     * first time, once MediaStore has been read.
     */
    suspend fun trackedOrDefaults(folders: List<DeviceFolder>): Set<String> {
        val current = prefs.first()
        if (current[DEFAULTS_APPLIED] == true) return current[TRACKED].orEmpty()
        val defaults = folders.filter { it.classification.trackedByDefault }.map { it.key }.toSet()
        var result = defaults
        dataStore.edit { p ->
            if (p[DEFAULTS_APPLIED] == true) {
                result = p[TRACKED].orEmpty()
            } else {
                p[TRACKED] = defaults
                p[DEFAULTS_APPLIED] = true
            }
        }
        return result
    }

    suspend fun setTracked(key: String, tracked: Boolean) {
        dataStore.edit { p ->
            val current = p[TRACKED].orEmpty()
            p[TRACKED] = if (tracked) current + key else current - key
            p[DEFAULTS_APPLIED] = true
        }
    }

    /** After unpairing: the next pairing starts again from the recommended folders. */
    suspend fun clear() {
        dataStore.edit { p -> p.clear() }
    }

    private companion object {
        val TRACKED = stringSetPreferencesKey("tracked")
        val DEFAULTS_APPLIED = booleanPreferencesKey("defaults_applied")
    }
}
