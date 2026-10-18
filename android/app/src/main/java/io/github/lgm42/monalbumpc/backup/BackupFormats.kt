package io.github.lgm42.monalbumpc.backup

import java.io.IOException
import java.io.InputStream
import java.security.MessageDigest
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.temporal.ChronoUnit

/** Dates of the protocol: ISO 8601 with offset, to the second (protocol §1). */
object IsoDates {

    fun format(epochMillis: Long, zone: ZoneId = ZoneId.systemDefault()): String =
        OffsetDateTime.ofInstant(Instant.ofEpochMilli(epochMillis), zone)
            .truncatedTo(ChronoUnit.SECONDS)
            .format(DateTimeFormatter.ISO_OFFSET_DATE_TIME)

    fun formatOrNull(epochMillis: Long?, zone: ZoneId = ZoneId.systemDefault()): String? =
        epochMillis?.takeIf { it > 0 }?.let { format(it, zone) }
}

/** SHA-256 of the original bytes, lowercase hex (protocol §1). */
object Sha256 {

    const val BUFFER_SIZE = 1 shl 20

    fun hex(input: InputStream, onProgress: ((Long) -> Unit)? = null): String {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(BUFFER_SIZE)
        var total = 0L
        while (true) {
            val read = input.read(buffer)
            if (read < 0) break
            digest.update(buffer, 0, read)
            total += read
            onProgress?.invoke(total)
        }
        return toHex(digest.digest())
    }

    fun toHex(bytes: ByteArray): String {
        val chars = CharArray(bytes.size * 2)
        for (i in bytes.indices) {
            val v = bytes[i].toInt() and 0xFF
            chars[i * 2] = HEX[v ushr 4]
            chars[i * 2 + 1] = HEX[v and 0x0F]
        }
        return String(chars)
    }

    private val HEX = "0123456789abcdef".toCharArray()
}

object Streams {

    /**
     * Skips exactly [count] bytes. InputStream.skip may skip less than asked (or nothing),
     * so fall back to reading. Throws if the stream ends first.
     */
    fun skipFully(input: InputStream, count: Long) {
        var remaining = count
        val scratch = ByteArray(minOf(count, 64L * 1024).toInt().coerceAtLeast(1))
        while (remaining > 0) {
            val skipped = input.skip(remaining)
            if (skipped > 0) {
                remaining -= skipped
                continue
            }
            val read = input.read(scratch, 0, minOf(remaining, scratch.size.toLong()).toInt())
            if (read < 0) throw IOException("End of stream after ${count - remaining} of $count bytes")
            remaining -= read
        }
    }
}
