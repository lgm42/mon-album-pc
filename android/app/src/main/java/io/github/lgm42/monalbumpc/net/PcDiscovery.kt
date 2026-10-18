package io.github.lgm42.monalbumpc.net

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.net.wifi.WifiManager
import android.os.Build
import java.net.Inet4Address
import java.net.InetAddress
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow

/** A PC found on the local network by mDNS (protocol §2). */
data class DiscoveredPc(
    /** mDNS service name, unique on the network. */
    val key: String,
    val pcId: String?,
    val name: String,
    val host: String,
    val port: Int,
)

/**
 * Finds the PCs running Mon Album PC on the Wi-Fi. Discovery runs while the flow is collected
 * and stops as soon as the collector is cancelled.
 */
class PcDiscovery(context: Context) {

    private val appContext = context.applicationContext

    fun discover(): Flow<List<DiscoveredPc>> = callbackFlow {
        val nsd = appContext.getSystemService(Context.NSD_SERVICE) as NsdManager
        val wifi = appContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val multicastLock = wifi.createMulticastLock("monalbumpc-discovery")
        multicastLock.setReferenceCounted(false)
        multicastLock.acquire()

        val guard = Any()
        val found = LinkedHashMap<String, DiscoveredPc>()
        val pending = ArrayDeque<NsdServiceInfo>()
        var resolving = false
        var stopped = false

        fun publish() {
            trySend(found.values.toList())
        }

        // NsdManager resolves only one service at a time before Android 14: queue them.
        fun resolveNext() {
            if (resolving || stopped) return
            val next = pending.removeFirstOrNull() ?: return
            resolving = true
            val listener = object : NsdManager.ResolveListener {
                override fun onResolveFailed(serviceInfo: NsdServiceInfo, errorCode: Int) {
                    synchronized(guard) {
                        resolving = false
                        resolveNext()
                    }
                }

                override fun onServiceResolved(serviceInfo: NsdServiceInfo) {
                    synchronized(guard) {
                        resolving = false
                        val pc = toDiscoveredPc(serviceInfo)
                        if (pc != null && !stopped) {
                            found[pc.key] = pc
                            publish()
                        }
                        resolveNext()
                    }
                }
            }
            try {
                // Deprecated in API 34 in favor of registerServiceInfoCallback, still functional.
                nsd.resolveService(next, listener)
            } catch (e: IllegalArgumentException) {
                resolving = false
            }
        }

        val discoveryListener = object : NsdManager.DiscoveryListener {
            override fun onStartDiscoveryFailed(serviceType: String, errorCode: Int) {
                publish()
            }

            override fun onStopDiscoveryFailed(serviceType: String, errorCode: Int) = Unit

            override fun onDiscoveryStarted(serviceType: String) = Unit

            override fun onDiscoveryStopped(serviceType: String) = Unit

            override fun onServiceFound(serviceInfo: NsdServiceInfo) {
                synchronized(guard) {
                    if (pending.none { it.serviceName == serviceInfo.serviceName }) {
                        pending.addLast(serviceInfo)
                    }
                    resolveNext()
                }
            }

            override fun onServiceLost(serviceInfo: NsdServiceInfo) {
                synchronized(guard) {
                    pending.removeAll { it.serviceName == serviceInfo.serviceName }
                    if (found.remove(serviceInfo.serviceName) != null) publish()
                }
            }
        }

        publish()
        nsd.discoverServices(SERVICE_TYPE, NsdManager.PROTOCOL_DNS_SD, discoveryListener)

        awaitClose {
            synchronized(guard) { stopped = true }
            try {
                nsd.stopServiceDiscovery(discoveryListener)
            } catch (e: IllegalArgumentException) {
                // Discovery never started.
            }
            if (multicastLock.isHeld) multicastLock.release()
        }
    }

    private fun toDiscoveredPc(info: NsdServiceInfo): DiscoveredPc? {
        val address = hostAddress(info) ?: return null
        val attributes = info.attributes
        val name = attributes["n"]?.let { String(it, Charsets.UTF_8) }?.takeIf { it.isNotBlank() }
            ?: info.serviceName
        val pcId = attributes["id"]?.let { String(it, Charsets.UTF_8) }?.takeIf { it.isNotBlank() }
        return DiscoveredPc(key = info.serviceName, pcId = pcId, name = name, host = address, port = info.port)
    }

    @Suppress("DEPRECATION")
    private fun hostAddress(info: NsdServiceInfo): String? {
        val address: InetAddress? = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            info.hostAddresses.firstOrNull { it is Inet4Address } ?: info.hostAddresses.firstOrNull()
        } else {
            info.host
        }
        return address?.hostAddress
    }

    companion object {
        const val SERVICE_TYPE = "_monalbumpc._tcp"
    }
}
