package io.github.lgm42.monalbumpc.media

/** A photo or video as listed by MediaStore. Dates are epoch milliseconds. */
data class DeviceMedia(
    val id: Long,
    /** content:// URI of the item. */
    val uri: String,
    val volume: String,
    val relativePath: String,
    val displayName: String,
    val size: Long,
    val dateModifiedMs: Long,
    val dateTakenMs: Long?,
    val mime: String,
    val isVideo: Boolean,
    val durationMs: Long,
) {
    val folderKey: String
        get() = FolderRules.key(volume, relativePath)

    /** Date used to sort and group: when the photo was taken, else when the file was modified. */
    val sortDateMs: Long
        get() = dateTakenMs?.takeIf { it > 0 } ?: dateModifiedMs
}

/** A folder that contains photos or videos. */
data class DeviceFolder(
    val key: String,
    val volume: String,
    val relativePath: String,
    val classification: FolderClass,
    val count: Int,
    /** URI of the most recent item, for the thumbnail. */
    val newestUri: String?,
    val newestIsVideo: Boolean,
) {
    val friendlyName: String
        get() = classification.friendlyName
}

object DeviceFolders {

    /** Groups media by folder, most populated first within each section. */
    fun fromMedia(media: List<DeviceMedia>): List<DeviceFolder> =
        media.groupBy { it.folderKey }.map { (key, items) ->
            val first = items.first()
            val newest = items.maxByOrNull { it.sortDateMs }
            DeviceFolder(
                key = key,
                volume = first.volume,
                relativePath = FolderRules.normalize(first.relativePath),
                classification = FolderRules.classify(first.relativePath, first.volume),
                count = items.size,
                newestUri = newest?.uri,
                newestIsVideo = newest?.isVideo == true,
            )
        }.sortedWith(compareBy<DeviceFolder>({ it.classification.section.ordinal }, { -it.count }, { it.friendlyName }))
}
