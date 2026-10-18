package io.github.lgm42.monalbumpc.net

import io.github.lgm42.monalbumpc.pairing.Fingerprints
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.TrustManager
import javax.net.ssl.X509TrustManager
import okhttp3.OkHttpClient

/**
 * Trusts the PC's self-signed certificate by pinning its fingerprint (protocol §1, §3).
 * With no expected fingerprint (6-digit code path), the first certificate seen is accepted
 * and recorded in [seenFingerprint] so that it can be checked against the pairing response.
 */
class PinnedTrust(private val expectedFingerprint: String?) {

    @Volatile
    var seenFingerprint: String? = null
        private set

    @Volatile
    var mismatchDetected: Boolean = false
        private set

    private val trustManager = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            throw CertificateException("Client certificates are not supported")
        }

        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            val leaf = chain?.firstOrNull() ?: throw CertificateException("Empty certificate chain")
            val fingerprint = Fingerprints.of(leaf.encoded)
            if (expectedFingerprint != null && !Fingerprints.matches(fingerprint, expectedFingerprint)) {
                mismatchDetected = true
                throw CertificateException("Unexpected certificate fingerprint")
            }
            seenFingerprint = fingerprint
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
    }

    fun newClient(): OkHttpClient {
        val sslContext = SSLContext.getInstance("TLS")
        sslContext.init(null, arrayOf<TrustManager>(trustManager), null)
        return OkHttpClient.Builder()
            .sslSocketFactory(sslContext.socketFactory, trustManager)
            // The certificate itself is pinned, its host name is irrelevant (it is reached by IP).
            .hostnameVerifier { _, _ -> true }
            .connectTimeout(5, TimeUnit.SECONDS)
            .readTimeout(20, TimeUnit.SECONDS)
            .writeTimeout(20, TimeUnit.SECONDS)
            .build()
    }
}
