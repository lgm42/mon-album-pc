package io.github.lgm42.monalbumpc.backup

import io.github.lgm42.monalbumpc.data.media.MediaEntity
import io.github.lgm42.monalbumpc.data.media.MediaState
import io.github.lgm42.monalbumpc.media.DeviceMedia

/** Changes to apply to the local index so that it matches MediaStore for the tracked folders. */
data class IndexDiff(val upserts: List<MediaEntity>, val deletions: List<Long>) {
    val isEmpty: Boolean
        get() = upserts.isEmpty() && deletions.isEmpty()
}

object IndexSync {

    fun diff(existing: List<MediaEntity>, current: List<DeviceMedia>, tracked: Set<String>): IndexDiff {
        val known = existing.associateBy { it.mediaStoreId }
        val wanted = current.filter { it.folderKey in tracked }
        val wantedIds = HashSet<Long>(wanted.size * 2)
        val upserts = ArrayList<MediaEntity>()
        for (media in wanted) {
            wantedIds.add(media.id)
            val old = known[media.id]
            when {
                old == null -> upserts.add(newEntity(media))
                old.size != media.size || old.dateModified != media.dateModifiedMs -> {
                    // Content changed: hash again and save again (the PC deduplicates).
                    upserts.add(newEntity(media))
                }
                old.relativePath != media.relativePath || old.displayName != media.displayName ||
                    old.volume != media.volume || old.uri != media.uri || old.dateTaken != media.dateTakenMs -> {
                    // Moved or renamed: same bytes, keep the hash and the state.
                    upserts.add(
                        old.copy(
                            uri = media.uri,
                            volume = media.volume,
                            relativePath = media.relativePath,
                            displayName = media.displayName,
                            dateTaken = media.dateTakenMs,
                        ),
                    )
                }
            }
        }
        val deletions = existing.filter { it.mediaStoreId !in wantedIds }.map { it.mediaStoreId }
        return IndexDiff(upserts, deletions)
    }

    fun newEntity(media: DeviceMedia) = MediaEntity(
        mediaStoreId = media.id,
        uri = media.uri,
        volume = media.volume,
        relativePath = media.relativePath,
        displayName = media.displayName,
        size = media.size,
        dateModified = media.dateModifiedMs,
        dateTaken = media.dateTakenMs,
        mime = media.mime,
        isVideo = media.isVideo,
        durationMs = media.durationMs,
        sha256 = null,
        state = MediaState.PENDING,
        errorReason = null,
        uploadId = null,
        savedAt = null,
    )
}
