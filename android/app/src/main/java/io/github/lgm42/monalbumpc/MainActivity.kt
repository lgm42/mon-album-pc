package io.github.lgm42.monalbumpc

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.viewModels
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import io.github.lgm42.monalbumpc.ui.AppUiState
import io.github.lgm42.monalbumpc.ui.AppViewModel
import io.github.lgm42.monalbumpc.ui.navigation.MonAlbumPcApp
import io.github.lgm42.monalbumpc.ui.onboarding.OnboardingScreen
import io.github.lgm42.monalbumpc.ui.theme.MonAlbumPcTheme

class MainActivity : ComponentActivity() {

    private val appViewModel: AppViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge()
        super.onCreate(savedInstanceState)
        // On recreation the intent is the same one, already handled.
        if (savedInstanceState == null) appViewModel.onDeepLink(intent?.data?.toString())

        setContent {
            MonAlbumPcTheme {
                val uiState by appViewModel.uiState.collectAsStateWithLifecycle()
                val pendingOffer by appViewModel.pendingOffer.collectAsStateWithLifecycle()
                when (val state = uiState) {
                    AppUiState.Loading -> Box(
                        modifier = Modifier
                            .fillMaxSize()
                            .background(MaterialTheme.colorScheme.background),
                    )
                    is AppUiState.Onboarding -> OnboardingScreen(
                        generation = state.generation,
                        pendingOffer = pendingOffer,
                        onOfferConsumed = appViewModel::consumePendingOffer,
                        onFinished = appViewModel::finishOnboarding,
                    )
                    is AppUiState.Main -> MonAlbumPcApp(
                        pc = state.pc,
                        onUnpair = { appViewModel.unpair(state.pc) },
                    )
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        appViewModel.onDeepLink(intent.data?.toString())
    }
}
