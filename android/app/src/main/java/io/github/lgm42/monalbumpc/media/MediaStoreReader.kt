package io.github.lgm42.monalbumpc.media

import android.content.ContentUris
import android.content.Context
import android.database.Cursor
import android.net.Uri
import android.provider.MediaStore
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Lists the photos and videos of every volume (internal storage and SD card). */
class MediaStoreReader(context: Context) {

    private val resolver = context.applicationContext.contentResolver

    suspend fun readAll(): List<DeviceMedia> = withContext(Dispatchers.IO) {
        val images = query(MediaStore.Images.Media.getContentUri(MediaStore.VOLUME_EXTERNAL), isVideo = false)
        val videos = query(MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL), isVideo = true)
        images + videos
    }

    private fun query(collection: Uri, isVideo: Boolean): List<DeviceMedia> {
        val projection = mutableListOf(
            MediaStore.MediaColumns._ID,
            MediaStore.MediaColumns.VOLUME_NAME,
            MediaStore.MediaColumns.RELATIVE_PATH,
            MediaStore.MediaColumns.DISPLAY_NAME,
            MediaStore.MediaColumns.SIZE,
            MediaStore.MediaColumns.DATE_MODIFIED,
            MediaStore.MediaColumns.MIME_TYPE,
            if (isVideo) MediaStore.Video.VideoColumns.DATE_TAKEN else MediaStore.Images.ImageColumns.DATE_TAKEN,
        )
        if (isVideo) projection.add(MediaStore.Video.VideoColumns.DURATION)

        val result = ArrayList<DeviceMedia>()
        val cursor: Cursor? = try {
            resolver.query(collection, projection.toTypedArray(), null, null, null)
        } catch (e: SecurityException) {
            null
        }
        cursor?.use { c ->
            val idCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns._ID)
            val volumeCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.VOLUME_NAME)
            val pathCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.RELATIVE_PATH)
            val nameCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.DISPLAY_NAME)
            val sizeCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.SIZE)
            val modifiedCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.DATE_MODIFIED)
            val mimeCol = c.getColumnIndexOrThrow(MediaStore.MediaColumns.MIME_TYPE)
            val takenCol = c.getColumnIndex(
                if (isVideo) MediaStore.Video.VideoColumns.DATE_TAKEN else MediaStore.Images.ImageColumns.DATE_TAKEN,
            )
            val durationCol = if (isVideo) c.getColumnIndex(MediaStore.Video.VideoColumns.DURATION) else -1

            while (c.moveToNext()) {
                val id = c.getLong(idCol)
                val volume = c.getString(volumeCol) ?: MediaStore.VOLUME_EXTERNAL_PRIMARY
                val relativePath = c.getString(pathCol) ?: continue
                val size = c.getLong(sizeCol)
                // Files that are only in the cloud, or still being written, have no bytes yet.
                if (size <= 0) continue
                val itemUri = ContentUris.withAppendedId(
                    if (isVideo) MediaStore.Video.Media.getContentUri(volume) else MediaStore.Images.Media.getContentUri(volume),
                    id,
                )
                result.add(
                    DeviceMedia(
                        id = id,
                        uri = itemUri.toString(),
                        volume = volume,
                        relativePath = relativePath,
                        displayName = c.getString(nameCol) ?: "media_$id",
                        size = size,
                        dateModifiedMs = c.getLong(modifiedCol) * 1000L,
                        dateTakenMs = if (takenCol >= 0 && !c.isNull(takenCol)) c.getLong(takenCol).takeIf { it > 0 } else null,
                        mime = c.getString(mimeCol) ?: if (isVideo) "video/*" else "image/*",
                        isVideo = isVideo,
                        durationMs = if (durationCol >= 0 && !c.isNull(durationCol)) c.getLong(durationCol) else 0L,
                    ),
                )
            }
        }
        return result
    }

    companion object {
        /** Collections to observe for changes (new photo, deletion…). */
        val observedUris: List<Uri>
            get() = listOf(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, MediaStore.Video.Media.EXTERNAL_CONTENT_URI)
    }
}
