package io.github.lgm42.monalbumpc.ui.onboarding

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import io.github.lgm42.monalbumpc.data.DeviceInfoFactory
import io.github.lgm42.monalbumpc.data.PairedPc
import io.github.lgm42.monalbumpc.data.PairingStore
import io.github.lgm42.monalbumpc.net.ApiError
import io.github.lgm42.monalbumpc.net.ApiException
import io.github.lgm42.monalbumpc.net.DiscoveredPc
import io.github.lgm42.monalbumpc.net.PairRequest
import io.github.lgm42.monalbumpc.net.PcApi
import io.github.lgm42.monalbumpc.net.PcDiscovery
import io.github.lgm42.monalbumpc.pairing.Fingerprints
import io.github.lgm42.monalbumpc.pairing.PairingCode
import io.github.lgm42.monalbumpc.pairing.PairingLink
import io.github.lgm42.monalbumpc.pairing.PairingOffer
import io.github.lgm42.monalbumpc.update.UpdateManager
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

sealed interface OnboardingStep {
    data object Welcome : OnboardingStep
    data object PhotosPermission : OnboardingStep
    data class PartialAccess(val denied: Boolean) : OnboardingStep
    data object Notifications : OnboardingStep
    data object Scan : OnboardingStep
    data object Code : OnboardingStep
    data object Pairing : OnboardingStep
    data class Success(val pcName: String, val albumName: String) : OnboardingStep
    data object QrInvalid : OnboardingStep
    data object Unreachable : OnboardingStep
    data object Failed : OnboardingStep
}

data class OnboardingUiState(
    val step: OnboardingStep = OnboardingStep.Welcome,
    val code: String = "",
    val codeError: Boolean = false,
    val pcs: List<DiscoveredPc> = emptyList(),
    val selectedPcKey: String? = null,
) {
    /** The chosen PC, or the only one found. */
    val selectedPc: DiscoveredPc?
        get() = pcs.firstOrNull { it.key == selectedPcKey } ?: pcs.singleOrNull()

    val canSubmitCode: Boolean
        get() = PairingCode.isComplete(code) && selectedPc != null
}

class OnboardingViewModel(application: Application) : AndroidViewModel(application) {

    private val store = PairingStore(application)
    private val discovery = PcDiscovery(application)

    private val _state = MutableStateFlow(OnboardingUiState())
    val state: StateFlow<OnboardingUiState> = _state.asStateFlow()

    private var pairingJob: Job? = null
    private var lastAttempt: Attempt? = null

    private sealed interface Attempt {
        data class Qr(val offer: PairingOffer) : Attempt
        data class Code(val pc: DiscoveredPc, val code: String) : Attempt
    }

    private fun go(step: OnboardingStep) {
        _state.update { it.copy(step = step) }
    }

    fun start() = go(OnboardingStep.PhotosPermission)

    fun onPhotoAccess(access: PhotoAccess) {
        when (access) {
            PhotoAccess.Full -> continueToNotifications()
            PhotoAccess.Partial -> go(OnboardingStep.PartialAccess(denied = false))
            PhotoAccess.Denied -> go(OnboardingStep.PartialAccess(denied = true))
        }
    }

    fun continueToNotifications() {
        if (MediaPermissions.needsNotificationPermission(getApplication<Application>())) {
            go(OnboardingStep.Notifications)
        } else {
            go(OnboardingStep.Scan)
        }
    }

    fun onNotificationsDone() = go(OnboardingStep.Scan)

    fun useCode() {
        _state.update { it.copy(step = OnboardingStep.Code, codeError = false) }
    }

    fun rescan() = go(OnboardingStep.Scan)

    /** Handles the system back button; returns false when it should leave the app. */
    fun canGoBack(step: OnboardingStep): Boolean = when (step) {
        OnboardingStep.Code, OnboardingStep.QrInvalid, OnboardingStep.Unreachable, OnboardingStep.Failed -> true
        else -> false
    }

    fun back() {
        if (canGoBack(_state.value.step)) go(OnboardingStep.Scan)
    }

    fun onQrText(text: String) {
        if (_state.value.step != OnboardingStep.Scan) return
        val offer = PairingLink.parse(text) ?: return
        pairWithOffer(offer)
    }

    fun pairWithOffer(offer: PairingOffer) = startPairing(Attempt.Qr(offer))

    fun onCodeChange(code: String) {
        _state.update { it.copy(code = PairingCode.sanitize(code), codeError = false) }
    }

    fun selectPc(key: String) {
        _state.update { it.copy(selectedPcKey = key) }
    }

    fun submitCode() {
        val current = _state.value
        val pc = current.selectedPc ?: return
        if (!PairingCode.isComplete(current.code)) return
        startPairing(Attempt.Code(pc, current.code))
    }

    fun retry() {
        val attempt = lastAttempt
        if (attempt != null) startPairing(attempt) else go(OnboardingStep.Scan)
    }

    /** Discovers the PCs while the code screen is shown; cancel the caller to stop. */
    suspend fun runDiscovery() {
        discovery.discover().collect { pcs -> _state.update { it.copy(pcs = pcs) } }
    }

    private fun startPairing(attempt: Attempt) {
        if (pairingJob?.isActive == true) return
        lastAttempt = attempt
        go(OnboardingStep.Pairing)
        pairingJob = viewModelScope.launch {
            val next = try {
                pair(attempt)
            } catch (e: ApiException) {
                stepForError(e.error, attempt)
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                OnboardingStep.Failed
            }
            if (next == OnboardingStep.Code) {
                _state.update { it.copy(step = OnboardingStep.Code, codeError = true) }
            } else {
                go(next)
            }
        }
    }

    private suspend fun pair(attempt: Attempt): OnboardingStep {
        val device = DeviceInfoFactory.create(getApplication<Application>())
        val host = when (attempt) {
            is Attempt.Qr -> attempt.offer.host
            is Attempt.Code -> attempt.pc.host
        }
        val port = when (attempt) {
            is Attempt.Qr -> attempt.offer.apiPort
            is Attempt.Code -> attempt.pc.port
        }
        // Code path: no fingerprint yet, trust on first use, checked against the response below.
        val expectedFingerprint = when (attempt) {
            is Attempt.Qr -> attempt.offer.fingerprint
            is Attempt.Code -> null
        }
        val request = when (attempt) {
            is Attempt.Qr -> PairRequest(token = attempt.offer.token, device = device)
            is Attempt.Code -> PairRequest(code = attempt.code, device = device)
        }
        val api = PcApi(host, port, expectedFingerprint)
        val response = api.pair(request)
        if (!Fingerprints.matches(api.seenFingerprint, response.certSha256)) return OnboardingStep.Failed

        val paired = PairedPc(
            pcId = response.pcId,
            pcName = response.pcName,
            albumName = response.albumName,
            host = host,
            apiPort = port,
            certSha256 = response.certSha256,
            deviceId = response.deviceId,
            secret = response.secret,
        )
        store.save(paired)
        // Does the PC offer a newer app? One status call with the new credentials, in the background.
        UpdateManager.checkAsync(getApplication<Application>(), paired, force = true)
        return OnboardingStep.Success(response.pcName, response.albumName)
    }

    private fun stepForError(error: ApiError, attempt: Attempt): OnboardingStep = when (error) {
        ApiError.PairingInvalid, ApiError.PairingExpired ->
            if (attempt is Attempt.Code) OnboardingStep.Code else OnboardingStep.QrInvalid
        is ApiError.Network -> OnboardingStep.Unreachable
        else -> OnboardingStep.Failed
    }
}
