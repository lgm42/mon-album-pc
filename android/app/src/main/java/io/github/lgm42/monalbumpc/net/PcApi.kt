package io.github.lgm42.monalbumpc.net

import java.io.IOException
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody

/**
 * Client of the PC API (protocol §3.3 and §5), at `https://<host>:<port>/api/v1/`.
 * Every call throws [ApiException] on failure.
 */
class PcApi(host: String, port: Int, expectedFingerprint: String?) {

    private val trust = PinnedTrust(expectedFingerprint)
    private val client: OkHttpClient = trust.newClient()

    // Uploads stream large files, and the PC checks the hash and stores the file before answering.
    private val uploadClient: OkHttpClient by lazy {
        client.newBuilder()
            .readTimeout(10, TimeUnit.MINUTES)
            .writeTimeout(2, TimeUnit.MINUTES)
            .build()
    }
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

    suspend fun check(authorization: String, request: CheckRequest): CheckResponse {
        val body = json.encodeToString(CheckRequest.serializer(), request).toRequestBody(JSON_TYPE)
        val httpRequest = newRequest("check")
            .header("Authorization", authorization)
            .post(body)
            .build()
        return execute(httpRequest, CheckResponse.serializer())
    }

    suspend fun checkNames(authorization: String, request: NameCheckRequest): NameCheckResponse {
        val body = json.encodeToString(NameCheckRequest.serializer(), request).toRequestBody(JSON_TYPE)
        val httpRequest = newRequest("check-names")
            .header("Authorization", authorization)
            .post(body)
            .build()
        return execute(httpRequest, NameCheckResponse.serializer())
    }

    suspend fun createUpload(authorization: String, request: UploadRequest): UploadResponse {
        val body = json.encodeToString(UploadRequest.serializer(), request).toRequestBody(JSON_TYPE)
        val httpRequest = newRequest("uploads")
            .header("Authorization", authorization)
            .post(body)
            .build()
        return execute(httpRequest, UploadResponse.serializer(), uploadClient)
    }

    /**
     * Sends bytes from [offset]. On `409 offset_mismatch`, throws [ApiError.OffsetMismatch]
     * with the offset expected by the PC (from the `Upload-Offset` header).
     */
    suspend fun patchUpload(authorization: String, uploadId: String, offset: Long, body: RequestBody): OffsetResponse {
        val httpRequest = newRequest("uploads/" + uploadPath(uploadId))
            .header("Authorization", authorization)
            .header("Upload-Offset", offset.toString())
            .patch(body)
            .build()
        val raw = call(httpRequest, uploadClient)
        if (raw.code !in 200..299) {
            val error = parseError(raw.code, raw.body)
            if (error is ApiError.OffsetMismatch) {
                throw ApiException(ApiError.OffsetMismatch(raw.uploadOffset))
            }
            throw ApiException(error)
        }
        return decode(raw.body, OffsetResponse.serializer())
    }

    /** Offset received by the PC for an upload, or null if it expired (404). */
    suspend fun uploadOffset(authorization: String, uploadId: String): Long? {
        val httpRequest = newRequest("uploads/" + uploadPath(uploadId))
            .header("Authorization", authorization)
            .head()
            .build()
        val raw = call(httpRequest, client)
        if (raw.code == 404) return null
        if (raw.code !in 200..299) throw ApiException(parseError(raw.code, raw.body))
        return raw.uploadOffset ?: throw ApiException(ApiError.InvalidResponse)
    }

    private fun newRequest(path: String): Request.Builder = try {
        Request.Builder().url(baseUrl + path)
    } catch (e: IllegalArgumentException) {
        throw ApiException(ApiError.Network("Invalid address: $baseUrl"))
    }

    private suspend fun <T> execute(
        request: Request,
        serializer: KSerializer<T>,
        httpClient: OkHttpClient = client,
    ): T = decode(executeRaw(request, httpClient), serializer)

    private fun <T> decode(text: String, serializer: KSerializer<T>): T = try {
        json.decodeFromString(serializer, text)
    } catch (e: SerializationException) {
        throw ApiException(ApiError.InvalidResponse)
    } catch (e: IllegalArgumentException) {
        throw ApiException(ApiError.InvalidResponse)
    }

    /** Runs the call and returns the body of a 2xx response. */
    private suspend fun executeRaw(request: Request, httpClient: OkHttpClient = client): String {
        val raw = call(request, httpClient)
        if (raw.code !in 200..299) throw ApiException(parseError(raw.code, raw.body))
        return raw.body
    }

    private class RawResponse(val code: Int, val body: String, val uploadOffset: Long?)

    private suspend fun call(request: Request, httpClient: OkHttpClient): RawResponse = withContext(Dispatchers.IO) {
        try {
            httpClient.newCall(request).execute().use { response ->
                RawResponse(
                    code = response.code,
                    body = response.body?.string().orEmpty(),
                    uploadOffset = response.header("Upload-Offset")?.trim()?.toLongOrNull(),
                )
            }
        } catch (e: IOException) {
            val error = if (trust.mismatchDetected) ApiError.CertificateMismatch else ApiError.Network(e.message)
            throw ApiException(error)
        }
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

        /** Upload ids come from the PC; keep them inside a single path segment. */
        fun uploadPath(uploadId: String): String = java.net.URLEncoder.encode(uploadId, "UTF-8").replace("+", "%20")
    }
}
