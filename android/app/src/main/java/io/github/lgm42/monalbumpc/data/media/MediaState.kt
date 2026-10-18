package io.github.lgm42.monalbumpc.data.media

/** Backup state of a media of a tracked folder. */
enum class MediaState { PENDING, UPLOADING, SAVED, ERROR }

/** Why a media could not be saved (stored in the index, shown in the viewer). */
object ErrorReasons {
    const val HASH_MISMATCH = "hash_mismatch"
    const val UNREADABLE = "unreadable"
    const val SERVER = "server"
    const val DISK_FULL = "disk_full"
    const val DESTINATION_UNAVAILABLE = "destination_unavailable"
}
