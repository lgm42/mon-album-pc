package io.github.lgm42.monalbumpc.pairing

import java.net.URI
import java.net.URISyntaxException
import java.net.URLDecoder

/**
 * Parses the pairing link of protocol §3.1, either as encoded in the QR code
 * (`http://<ip>:48621/p?t=…&f=…&h=…&n=…[&p=…]`) or as a deep link (`monalbumpc://pair?…`).
 */
object PairingLink {
    const val DEFAULT_API_PORT = 48620

    fun parse(text: String): PairingOffer? {
        val uri = try {
            URI(text.trim().replace(" ", "%20"))
        } catch (e: URISyntaxException) {
            return null
        }
        val scheme = uri.scheme?.lowercase() ?: return null
        val accepted = when (scheme) {
            "http", "https" -> uri.rawPath == "/p"
            "monalbumpc" -> uri.host?.lowercase() == "pair"
            else -> false
        }
        if (!accepted) return null

        val params = parseQuery(uri.rawQuery ?: return null)
        val token = params["t"]?.takeIf { it.isNotBlank() } ?: return null
        val fingerprint = params["f"]?.takeIf { it.isNotBlank() } ?: return null
        val host = params["h"]?.takeIf { it.isNotBlank() }
            ?: uri.host?.takeIf { scheme != "monalbumpc" && it.isNotBlank() }
            ?: return null
        val port = when (val p = params["p"]) {
            null, "" -> DEFAULT_API_PORT
            else -> p.toIntOrNull()?.takeIf { it in 1..65535 } ?: return null
        }
        val name = params["n"]?.takeIf { it.isNotBlank() }
        return PairingOffer(host = host, apiPort = port, token = token, fingerprint = fingerprint, pcName = name)
    }

    private fun parseQuery(rawQuery: String): Map<String, String> {
        val result = HashMap<String, String>()
        for (pair in rawQuery.split('&')) {
            if (pair.isEmpty()) continue
            val eq = pair.indexOf('=')
            val key = if (eq >= 0) pair.substring(0, eq) else pair
            val value = if (eq >= 0) pair.substring(eq + 1) else ""
            val decodedKey = decode(key) ?: continue
            val decodedValue = decode(value) ?: continue
            if (!result.containsKey(decodedKey)) result[decodedKey] = decodedValue
        }
        return result
    }

    private fun decode(value: String): String? = try {
        URLDecoder.decode(value, "UTF-8")
    } catch (e: IllegalArgumentException) {
        null
    }
}
