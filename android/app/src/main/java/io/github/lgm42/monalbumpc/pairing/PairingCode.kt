package io.github.lgm42.monalbumpc.pairing

/** The 6-digit code shown by the PC under the QR code (protocol §3.2). */
object PairingCode {
    const val LENGTH = 6

    /** Keeps the digits only, at most [LENGTH] of them (typing, pasting "123 456"…). */
    fun sanitize(input: String): String = input.filter { it in '0'..'9' }.take(LENGTH)

    fun isComplete(code: String): Boolean = code.length == LENGTH && code.all { it in '0'..'9' }

    /** "123456" -> "123 456", as displayed by the PC. */
    fun format(code: String): String =
        if (code.length > 3) code.substring(0, 3) + " " + code.substring(3) else code
}
