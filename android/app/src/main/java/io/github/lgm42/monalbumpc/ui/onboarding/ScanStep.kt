package io.github.lgm42.monalbumpc.ui.onboarding

import android.Manifest
import android.app.Activity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.RepeatMode
import androidx.compose.animation.core.animateFloat
import androidx.compose.animation.core.infiniteRepeatable
import androidx.compose.animation.core.rememberInfiniteTransition
import androidx.compose.animation.core.tween
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import androidx.core.view.WindowCompat
import io.github.lgm42.monalbumpc.R
import io.github.lgm42.monalbumpc.ui.components.boldTemplate

private val OnCamera = Color.White

/** Full-screen QR code scanner, with a link to the 6-digit code for phones without a camera. */
@Composable
fun ScanStep(onQrText: (String) -> Unit, onUseCode: () -> Unit) {
    val context = LocalContext.current
    var hasPermission by remember {
        mutableStateOf(MediaPermissions.isGranted(context, Manifest.permission.CAMERA))
    }
    var cameraFailed by remember { mutableStateOf(false) }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) { granted ->
        hasPermission = granted
    }
    LaunchedEffect(Unit) {
        if (!hasPermission) launcher.launch(Manifest.permission.CAMERA)
    }
    val showCamera = hasPermission && !cameraFailed
    LightStatusBarIcons()

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color.Black),
    ) {
        if (showCamera) {
            QrCameraPreview(
                onQrText = onQrText,
                onCameraError = { cameraFailed = true },
                modifier = Modifier.fillMaxSize(),
            )
        }
        // Darkens the preview so that the white text stays readable.
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color.Black.copy(alpha = 0.35f)),
        )
        Column(
            modifier = Modifier
                .fillMaxSize()
                .safeDrawingPadding()
                .padding(start = 24.dp, end = 24.dp, top = 24.dp, bottom = 16.dp),
        ) {
            Text(
                text = stringResource(R.string.scan_title),
                style = MaterialTheme.typography.headlineMedium,
                color = OnCamera,
            )
            Spacer(Modifier.height(12.dp))
            Text(
                text = boldTemplate(stringResource(R.string.scan_body), stringResource(R.string.app_name)),
                style = MaterialTheme.typography.bodyMedium,
                color = OnCamera,
            )
            Spacer(Modifier.height(36.dp))
            when {
                showCamera -> {
                    ScanFrame(modifier = Modifier.align(Alignment.CenterHorizontally))
                    Spacer(Modifier.height(16.dp))
                    Text(
                        text = stringResource(R.string.scan_frame_hint),
                        style = MaterialTheme.typography.bodySmall,
                        color = OnCamera.copy(alpha = 0.8f),
                    )
                }
                cameraFailed -> {
                    Text(
                        text = stringResource(R.string.scan_camera_unavailable),
                        style = MaterialTheme.typography.bodyLarge,
                        color = OnCamera,
                    )
                }
                else -> {
                    Text(
                        text = stringResource(R.string.scan_camera_needed),
                        style = MaterialTheme.typography.bodyLarge,
                        color = OnCamera,
                    )
                    Spacer(Modifier.height(20.dp))
                    PrimaryButton(
                        text = stringResource(R.string.scan_allow_camera),
                        onClick = { launcher.launch(Manifest.permission.CAMERA) },
                    )
                }
            }
            Spacer(Modifier.weight(1f))
            TextButton(
                onClick = onUseCode,
                modifier = Modifier
                    .fillMaxWidth()
                    .heightIn(min = 48.dp),
            ) {
                Text(
                    text = stringResource(R.string.scan_no_camera),
                    style = MaterialTheme.typography.bodyMedium,
                    color = OnCamera,
                    textDecoration = TextDecoration.Underline,
                )
            }
        }
    }
}

/** White status bar icons over the dark camera screen, restored when leaving it. */
@Composable
private fun LightStatusBarIcons() {
    val view = LocalView.current
    DisposableEffect(view) {
        val window = (view.context as? Activity)?.window
        val controller = if (window != null) WindowCompat.getInsetsController(window, view) else null
        val previous = controller?.isAppearanceLightStatusBars
        controller?.isAppearanceLightStatusBars = false
        onDispose {
            if (controller != null && previous != null) controller.isAppearanceLightStatusBars = previous
        }
    }
}

/** 280dp frame with four primary corners and an animated scan line. */
@Composable
private fun ScanFrame(modifier: Modifier = Modifier) {
    val color = MaterialTheme.colorScheme.primary
    val transition = rememberInfiniteTransition(label = "scan")
    val progress by transition.animateFloat(
        initialValue = 0f,
        targetValue = 1f,
        animationSpec = infiniteRepeatable(
            animation = tween(durationMillis = 2200, easing = LinearEasing),
            repeatMode = RepeatMode.Reverse,
        ),
        label = "scanLine",
    )
    Canvas(modifier = modifier.size(280.dp)) {
        val stroke = 4.dp.toPx()
        val arm = 44.dp.toPx()
        val half = stroke / 2f
        val w = size.width
        val h = size.height
        // Top-left
        drawLine(color, Offset(half, 0f), Offset(half, arm), strokeWidth = stroke)
        drawLine(color, Offset(0f, half), Offset(arm, half), strokeWidth = stroke)
        // Top-right
        drawLine(color, Offset(w - half, 0f), Offset(w - half, arm), strokeWidth = stroke)
        drawLine(color, Offset(w - arm, half), Offset(w, half), strokeWidth = stroke)
        // Bottom-left
        drawLine(color, Offset(half, h - arm), Offset(half, h), strokeWidth = stroke)
        drawLine(color, Offset(0f, h - half), Offset(arm, h - half), strokeWidth = stroke)
        // Bottom-right
        drawLine(color, Offset(w - half, h - arm), Offset(w - half, h), strokeWidth = stroke)
        drawLine(color, Offset(w - arm, h - half), Offset(w, h - half), strokeWidth = stroke)
        // Scan line
        val inset = 14.dp.toPx()
        val y = inset + (h - 2 * inset) * progress
        drawLine(color, Offset(inset, y), Offset(w - inset, y), strokeWidth = 2.dp.toPx())
    }
}
