package io.github.lgm42.monalbumpc.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.data.PairingStore
import io.github.lgm42.monalbumpc.net.ApiException
import io.github.lgm42.monalbumpc.net.PcApi
import io.github.lgm42.monalbumpc.pairing.PairingLink
import io.github.lgm42.monalbumpc.pairing.PairingOffer
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull

sealed interface AppUiState {
    data object Loading : AppUiState

    /** [generation] changes after each unpairing, so that the onboarding starts afresh. */
    data class Onboarding(val generation: Int) : AppUiState

    data class Main(val pc: PairedPc) : AppUiState
}

/** Top-level routing: onboarding until paired, then the main tabs. */
class AppViewModel(application: Application) : AndroidViewModel(application) {

    private val store = PairingStore(application)

    // Stays true from the first "unpaired" state until the user leaves the success screen,
    // so that saving the pairing does not skip that screen.
    private val onboardingActive = MutableStateFlow(false)
    private val generation = MutableStateFlow(0)

    private val _pendingOffer = MutableStateFlow<PairingOffer?>(null)

    /** Pairing offer received by deep link, waiting for the onboarding to use it. */
    val pendingOffer: StateFlow<PairingOffer?> = _pendingOffer.asStateFlow()

    val uiState: StateFlow<AppUiState> =
        combine(store.paired, onboardingActive, generation) { pc, active, gen ->
            if (pc == null || active) AppUiState.Onboarding(gen) else AppUiState.Main(pc)
        }.stateIn(viewModelScope, SharingStarted.Eagerly, AppUiState.Loading)

    init {
        viewModelScope.launch {
            store.paired.collect { pc -> if (pc == null) onboardingActive.value = true }
        }
    }

    fun onDeepLink(link: String?) {
        if (link == null) return
        val offer = PairingLink.parse(link) ?: return
        // Pairing again while paired needs an explicit unpairing first.
        if (uiState.value is AppUiState.Main) return
        _pendingOffer.value = offer
    }

    fun consumePendingOffer() {
        _pendingOffer.value = null
    }

    fun finishOnboarding() {
        onboardingActive.value = false
    }

    fun unpair(pc: PairedPc) {
        viewModelScope.launch {
            // Best effort: the phone forgets the PC even if the PC cannot be told.
            try {
                withTimeoutOrNull(UNPAIR_TIMEOUT_MS) {
                    PcApi(pc.host, pc.apiPort, pc.certSha256).unpair(pc.authorization)
                }
            } catch (e: ApiException) {
                // Ignored.
            }
            _pendingOffer.value = null
            generation.value = generation.value + 1
            store.clear()
        }
    }

    private companion object {
        const val UNPAIR_TIMEOUT_MS = 8_000L
    }
}
