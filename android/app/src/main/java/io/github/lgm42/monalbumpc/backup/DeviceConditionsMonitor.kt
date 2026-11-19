package io.github.lgm42.monalbumpc.backup

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.os.BatteryManager
import androidx.core.content.ContextCompat
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.distinctUntilChanged

/**
 * Network and charging state, to tell why an automatic backup waits ("En attente du Wi-Fi", "En attente de la charge"):
 * WorkManager keeps such work queued without saying which constraint is missing.
 */
object DeviceConditionsMonitor {

    fun observe(context: Context): Flow<DeviceConditions> {
        val appContext = context.applicationContext
        return combine(network(appContext), charging(appContext)) { network, charging ->
            DeviceConditions(connected = network.first, unmetered = network.second, charging = charging)
        }.distinctUntilChanged()
    }

    /** (connected, unmetered) of the default network. */
    private fun network(context: Context): Flow<Pair<Boolean, Boolean>> = callbackFlow {
        val manager = context.getSystemService(ConnectivityManager::class.java)
        if (manager == null) {
            trySend(true to true)
            awaitClose {}
            return@callbackFlow
        }
        // Like WorkManager's network constraint: a network with Internet that Android validated.
        fun of(caps: NetworkCapabilities?): Pair<Boolean, Boolean> {
            if (caps == null || !caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) ||
                !caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED)
            ) {
                return false to false
            }
            return true to caps.hasCapability(NetworkCapabilities.NET_CAPABILITY_NOT_METERED)
        }
        trySend(of(manager.getNetworkCapabilities(manager.activeNetwork)))
        val callback = object : ConnectivityManager.NetworkCallback() {
            override fun onCapabilitiesChanged(network: Network, caps: NetworkCapabilities) {
                trySend(of(caps))
            }

            override fun onLost(network: Network) {
                trySend(of(manager.getNetworkCapabilities(manager.activeNetwork)))
            }
        }
        try {
            manager.registerDefaultNetworkCallback(callback)
        } catch (e: RuntimeException) {
            // Too many callbacks or no permission: keep the first answer.
            awaitClose {}
            return@callbackFlow
        }
        awaitClose { manager.unregisterNetworkCallback(callback) }
    }.distinctUntilChanged()

    private fun charging(context: Context): Flow<Boolean> = callbackFlow {
        val receiver = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                trySend(isPlugged(intent))
            }
        }
        // ACTION_BATTERY_CHANGED is sticky: the registration returns the current state.
        val sticky = ContextCompat.registerReceiver(
            context,
            receiver,
            IntentFilter(Intent.ACTION_BATTERY_CHANGED),
            ContextCompat.RECEIVER_NOT_EXPORTED,
        )
        trySend(sticky != null && isPlugged(sticky))
        awaitClose { context.unregisterReceiver(receiver) }
    }.distinctUntilChanged()

    private fun isPlugged(intent: Intent): Boolean = intent.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) != 0
}
