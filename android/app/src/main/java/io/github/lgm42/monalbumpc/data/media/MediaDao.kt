package io.github.lgm42.monalbumpc.data.media

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import kotlinx.coroutines.flow.Flow

@Dao
interface MediaDao {

    @Query("SELECT * FROM media")
    fun observeAll(): Flow<List<MediaEntity>>

    @Query("SELECT * FROM media")
    suspend fun getAll(): List<MediaEntity>

    @Query("SELECT * FROM media WHERE mediaStoreId = :id")
    suspend fun get(id: Long): MediaEntity?

    /** Items still to save, oldest first (protocol §5.3). */
    @Query(
        "SELECT * FROM media WHERE state != 'SAVED' " +
            "ORDER BY CASE WHEN dateTaken IS NULL OR dateTaken <= 0 THEN dateModified ELSE dateTaken END ASC",
    )
    suspend fun unsavedOldestFirst(): List<MediaEntity>

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun upsert(items: List<MediaEntity>)

    @Query("DELETE FROM media WHERE mediaStoreId IN (:ids)")
    suspend fun deleteIds(ids: List<Long>)

    @Query("DELETE FROM media")
    suspend fun deleteAll()

    @Query("UPDATE media SET sha256 = :sha256 WHERE mediaStoreId = :id")
    suspend fun setHash(id: Long, sha256: String)

    @Query("UPDATE media SET state = 'SAVED', errorReason = NULL, uploadId = NULL, savedAt = :savedAt WHERE mediaStoreId IN (:ids)")
    suspend fun markSaved(ids: List<Long>, savedAt: Long)

    @Query("UPDATE media SET state = 'UPLOADING', uploadId = :uploadId WHERE mediaStoreId = :id")
    suspend fun markUploading(id: Long, uploadId: String?)

    @Query("UPDATE media SET state = 'PENDING', errorReason = :reason WHERE mediaStoreId = :id")
    suspend fun markPending(id: Long, reason: String?)

    @Query("UPDATE media SET state = 'ERROR', errorReason = :reason, uploadId = NULL WHERE mediaStoreId = :id")
    suspend fun markError(id: Long, reason: String)

    /** After a crash or a stopped worker, nothing is really uploading. */
    @Query("UPDATE media SET state = 'PENDING' WHERE state = 'UPLOADING'")
    suspend fun resetUploading()

    /** "Réessayer maintenant": errors go back to the queue. */
    @Query("UPDATE media SET state = 'PENDING', errorReason = NULL WHERE state = 'ERROR' AND mediaStoreId IN (:ids)")
    suspend fun retryErrors(ids: List<Long>)
}
