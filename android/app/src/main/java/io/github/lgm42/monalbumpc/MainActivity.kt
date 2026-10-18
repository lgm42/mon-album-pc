package io.github.lgm42.monalbumpc

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import io.github.lgm42.monalbumpc.ui.navigation.MonAlbumPcApp
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        setContent {
            MonAlbumPcTheme {
                MonAlbumPcApp()
            }
        }
    }
}
