package io.github.lgm42.monalbumpc.ui.onboarding

import android.content.ActivityNotFoundException
import android.content.Intent
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import io.github.lgm42.monalbumpc.pairing.PairingOffer

/**
 * First launch: welcome, permissions, then pairing with the PC by QR code or 6-digit code.
 * [generation] gives a fresh onboarding after each unpairing.
 */
@Composable
fun OnboardingScreen(
    generation: Int,
    pendingOffer: PairingOffer?,
    onOfferConsumed: () -> Unit,
    onFinished: () -> Unit,
) {
    val viewModel: OnboardingViewModel = viewModel(key = "onboarding-$generation")
    val state by viewModel.state.collectAsStateWithLifecycle()
    val context = LocalContext.current

    // A pairing link opened from the PC's download page skips straight to pairing.
    LaunchedEffect(pendingOffer) {
        if (pendingOffer != null) {
            viewModel.pairWithOffer(pendingOffer)
            onOfferConsumed()
        }
    }

    BackHandler(enabled = viewModel.canGoBack(state.step)) { viewModel.back() }

    when (val step = state.step) {
        OnboardingStep.Welcome -> WelcomeStep(onStart = viewModel::start)
        OnboardingStep.PhotosPermission -> PhotosPermissionStep(onResult = viewModel::onPhotoAccess)
        is OnboardingStep.PartialAccess -> PartialAccessStep(
            denied = step.denied,
            onResult = viewModel::onPhotoAccess,
            onContinue = viewModel::continueToNotifications,
        )
        OnboardingStep.Notifications -> NotificationsStep(onDone = viewModel::onNotificationsDone)
        OnboardingStep.Scan -> ScanStep(onQrText = viewModel::onQrText, onUseCode = viewModel::useCode)
        OnboardingStep.Code -> CodeStep(
            state = state,
            onCodeChange = viewModel::onCodeChange,
            onSelectPc = viewModel::selectPc,
            onSubmit = viewModel::submitCode,
            onBack = viewModel::rescan,
            discover = { viewModel.runDiscovery() },
        )
        OnboardingStep.Pairing -> PairingInProgress()
        is OnboardingStep.Success -> SuccessStep(
            pcName = step.pcName,
            albumName = step.albumName,
            onContinue = onFinished,
        )
        OnboardingStep.QrInvalid -> QrInvalidStep(onRescan = viewModel::rescan, onUseCode = viewModel::useCode)
        OnboardingStep.Unreachable -> UnreachableStep(
            onRetry = viewModel::retry,
            onOpenWifiSettings = {
                try {
                    context.startActivity(Intent(Settings.ACTION_WIFI_SETTINGS))
                } catch (e: ActivityNotFoundException) {
                    // No Wi-Fi settings screen on this device.
                }
            },
            onBack = viewModel::rescan,
        )
        OnboardingStep.Failed -> FailedStep(onRescan = viewModel::rescan, onUseCode = viewModel::useCode)
    }
}
