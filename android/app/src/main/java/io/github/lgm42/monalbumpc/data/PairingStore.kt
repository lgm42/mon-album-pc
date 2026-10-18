package io.github.lgm42.monalbumpc.data

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.emptyPreferences
import androidx.datastore.preferences.core.intPreferencesKey
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import io.github.lgm42.monalbumpc.net.PcApi
import java.io.IOException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.catch
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.withContext

/** The PC this phone is paired with. */
data class PairedPc(
    val pcId: String,
    val pcName: String,
    val albumName: String,
    val host: String,
    val apiPort: Int,
    val certSha256: String,
    val deviceId: String,
    val secret: String,
) {
    /** Value of the `Authorization` header (protocol §4). */
    val authorization: String
        get() = PcApi.bearer(deviceId, secret)

    // Never log the secret.
    override fun toString(): String = "PairedPc(pcId=$pcId, pcName=$pcName, host=$host:$apiPort, deviceId=$deviceId)"
}

private val Context.pairingDataStore: DataStore<Preferences> by preferencesDataStore(name = "pairing")

class PairingStore(context: Context) {

    private val dataStore = context.applicationContext.pairingDataStore

    val paired: Flow<PairedPc?> = dataStore.data
        .catch { e -> if (e is IOException) emit(emptyPreferences()) else throw e }
        .map { prefs -> read(prefs) }
        .distinctUntilChanged()
        .flowOn(Dispatchers.IO)

    suspend fun save(pc: PairedPc) {
        val encryptedSecret = withContext(Dispatchers.IO) { SecretCipher.encrypt(pc.secret) }
        dataStore.edit { prefs ->
            prefs[PC_ID] = pc.pcId
            prefs[PC_NAME] = pc.pcName
            prefs[ALBUM_NAME] = pc.albumName
            prefs[HOST] = pc.host
            prefs[API_PORT] = pc.apiPort
            prefs[CERT_SHA256] = pc.certSha256
            prefs[DEVICE_ID] = pc.deviceId
            prefs[SECRET] = encryptedSecret
        }
    }

    suspend fun clear() {
        dataStore.edit { prefs -> prefs.clear() }
    }

    private fun read(prefs: Preferences): PairedPc? {
        val encryptedSecret = prefs[SECRET] ?: return null
        val secret = SecretCipher.decrypt(encryptedSecret) ?: return null
        return PairedPc(
            pcId = prefs[PC_ID] ?: return null,
            pcName = prefs[PC_NAME] ?: return null,
            albumName = prefs[ALBUM_NAME] ?: return null,
            host = prefs[HOST] ?: return null,
            apiPort = prefs[API_PORT] ?: return null,
            certSha256 = prefs[CERT_SHA256] ?: return null,
            deviceId = prefs[DEVICE_ID] ?: return null,
            secret = secret,
        )
    }

    private companion object {
        val PC_ID = stringPreferencesKey("pc_id")
        val PC_NAME = stringPreferencesKey("pc_name")
        val ALBUM_NAME = stringPreferencesKey("album_name")
        val HOST = stringPreferencesKey("host")
        val API_PORT = intPreferencesKey("api_port")
        val CERT_SHA256 = stringPreferencesKey("cert_sha256")
        val DEVICE_ID = stringPreferencesKey("device_id")
        val SECRET = stringPreferencesKey("secret")
    }
}
