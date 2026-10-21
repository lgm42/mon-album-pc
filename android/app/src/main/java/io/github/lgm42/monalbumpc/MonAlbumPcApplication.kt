package io.github.lgm42.monalbumpc

import android.app.Application
import coil.ImageLoader
import coil.ImageLoaderFactory
import coil.decode.VideoFrameDecoder
import io.github.lgm42.monalbumpc.update.UpdateManager

class MonAlbumPcApplication : Application(), ImageLoaderFactory {

    override fun onCreate() {
        super.onCreate()
        // Removes the downloaded APK once the app has reached its version.
        UpdateManager.cleanupAsync(this)
    }

    /** Coil loader shared by every thumbnail; video thumbnails are decoded from a frame. */
    override fun newImageLoader(): ImageLoader = ImageLoader.Builder(this)
        .components { add(VideoFrameDecoder.Factory()) }
        .crossfade(true)
        .build()
}
