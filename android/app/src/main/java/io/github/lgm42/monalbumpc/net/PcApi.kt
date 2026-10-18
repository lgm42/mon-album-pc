package io.github.lgm42.monalbumpc.net

import java.io.IOException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody

/**
 * Client of the PC API (protocol §3.3 and §5), at `https://<host>:<port>/api/v1/`.
 * Every call throws [ApiException] on failure.
 */
class PcApi(host: String, port: Int, expectedFingerprint: String?) {

    private val trust = PinnedTrust(expectedFingerprint)
    private val client: OkHttpClient = trust.newClient()
    private val baseUrl = "https://${urlHost(host)}:$port/api/v1/"

    /** Fingerprint of the certificate presented by the PC during the last connection. */
    val seenFingerprint: String?
        get() = trust.seenFingerprint

    suspend fun pair(request: PairRequest): PairResponse {
        val body = json.encodeToString(PairRequest.serializer(), request).toRequestBody(JSON_TYPE)
        val httpRequest = newRequest("pair").post(body).build()
        return execute(httpRequest, PairResponse.serializer())
    }

    suspend fun status(authorization: String): StatusResponse {
        val httpRequest = newRequest("status")
            .header("Authorization", authorization)
            .get()
            .build()
        return execute(httpRequest, StatusResponse.serializer())
    }

    suspend fun unpair(authorization: String) {
        val httpRequest = newRequest("pairing")
            .header("Authorization", authorization)
            .delete()
            .build()
        executeRaw(httpRequest)
    }

    private fun newRequest(path: String): Request.Builder = try {
        Request.Builder().url(baseUrl + path)
    } catch (e: IllegalArgumentException) {
        throw ApiException(ApiError.Network("Invalid address: $baseUrl"))
    }

    private suspend fun <T> execute(request: Request, serializer: KSerializer<T>): T {
        val text = executeRaw(request)
        return try {
            json.decodeFromString(serializer, text)
        } catch (e: SerializationException) {
            throw ApiException(ApiError.InvalidResponse)
        } catch (e: IllegalArgumentException) {
            throw ApiException(ApiError.InvalidResponse)
        }
    }

    /** Runs the call and returns the body of a 2xx response. */
    private suspend fun executeRaw(request: Request): String = withContext(Dispatchers.IO) {
        val result: Pair<Int, String> = try {
            client.newCall(request).execute().use { response ->
                Pair(response.code, response.body?.string().orEmpty())
            }
        } catch (e: IOException) {
            val error = if (trust.mismatchDetected) ApiError.CertificateMismatch else ApiError.Network(e.message)
            throw ApiException(error)
        }
        val (code, text) = result
        if (code !in 200..299) throw ApiException(parseError(code, text))
        text
    }

    private fun parseError(status: Int, text: String): ApiError {
        val code = try {
            json.decodeFromString(ErrorEnvelope.serializer(), text).error?.code
        } catch (e: SerializationException) {
            null
        } catch (e: IllegalArgumentException) {
            null
        }
        return ApiError.fromCode(status, code?.takeIf { it.isNotEmpty() })
    }

    companion object {
        private val JSON_TYPE = "application/json; charset=utf-8".toMediaType()

        val json = Json {
            ignoreUnknownKeys = true
            encodeDefaults = true
        }

        /** IPv6 literals must be bracketed in a URL. */
        fun urlHost(host: String): String =
            if (host.contains(':') && !host.startsWith("[")) "[$host]" else host

        fun bearer(deviceId: String, secret: String): String = "Bearer $deviceId.$secret"
    }
}
