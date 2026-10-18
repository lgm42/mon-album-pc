package io.github.lgm42.monalbumpc.data.media

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import androidx.room.TypeConverter

/** A media of a tracked folder and its backup state. Dates are epoch milliseconds. */
@Entity(tableName = "media", indices = [Index(value = ["state"])])
data class MediaEntity(
    @PrimaryKey val mediaStoreId: Long,
    val uri: String,
    val volume: String,
    val relativePath: String,
    val displayName: String,
    val size: Long,
    val dateModified: Long,
    val dateTaken: Long?,
    val mime: String,
    val isVideo: Boolean,
    val durationMs: Long,
    val sha256: String?,
    val state: MediaState,
    val errorReason: String?,
    val uploadId: String?,
    val savedAt: Long?,
)

/** Date used to sort: when the photo was taken, else when the file was modified. */
fun MediaEntity.sortDate(): Long = dateTaken?.takeIf { it > 0 } ?: dateModified

class MediaConverters {
    @TypeConverter
    fun stateToString(state: MediaState): String = state.name

    @TypeConverter
    fun stringToState(value: String): MediaState =
        MediaState.entries.firstOrNull { it.name == value } ?: MediaState.PENDING
}
