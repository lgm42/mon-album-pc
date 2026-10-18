package io.github.lgm42.monalbumpc.backup

import java.io.IOException
import java.io.InputStream
import okhttp3.MediaType
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody
import okio.BufferedSink

/**
 * Streams the bytes of a file from [offset] to its end (PATCH /uploads/{id}, protocol §5.3),
 * without loading it in memory. [open] is called for each write (OkHttp may retry).
 */
class OffsetStreamBody(
    private val totalSize: Long,
    private val offset: Long,
    private val open: () -> InputStream,
    private val onBytesSent: ((Long) -> Unit)? = null,
) : RequestBody() {

    init {
        require(offset in 0..totalSize) { "offset $offset out of 0..$totalSize" }
    }

    override fun contentType(): MediaType = OCTET_STREAM

    override fun contentLength(): Long = totalSize - offset

    override fun writeTo(sink: BufferedSink) {
        open().use { input ->
            Streams.skipFully(input, offset)
            val buffer = ByteArray(BUFFER_SIZE)
            var remaining = totalSize - offset
            var sent = 0L
            while (remaining > 0) {
                val read = input.read(buffer, 0, minOf(remaining, buffer.size.toLong()).toInt())
                // The file shrank since it was declared: the PC would reject the hash anyway.
                if (read < 0) throw IOException("File ended $remaining bytes early")
                sink.write(buffer, 0, read)
                remaining -= read
                sent += read
                onBytesSent?.invoke(offset + sent)
            }
        }
    }

    companion object {
        val OCTET_STREAM: MediaType = "application/octet-stream".toMediaType()
        private const val BUFFER_SIZE = 256 * 1024
    }
}
