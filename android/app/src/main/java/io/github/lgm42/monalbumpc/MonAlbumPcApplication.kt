package io.github.lgm42.monalbumpc

import android.app.Application
import coil.ImageLoader
import coil.ImageLoaderFactory
import coil.decode.VideoFrameDecoder

class MonAlbumPcApplication : Application(), ImageLoaderFactory {

    /** Coil loader shared by every thumbnail; video thumbnails are decoded from a frame. */
    override fun newImageLoader(): ImageLoader = ImageLoader.Builder(this)
        .components { add(VideoFrameDecoder.Factory()) }
        .crossfade(true)
        .build()
}
