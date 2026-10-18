package io.github.lgm42.monalbumpc.ui.onboarding

import androidx.camera.core.CameraSelector
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import com.google.mlkit.vision.barcode.BarcodeScanner
import com.google.mlkit.vision.barcode.BarcodeScannerOptions
import com.google.mlkit.vision.barcode.BarcodeScanning
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.common.InputImage
import java.util.concurrent.Executors

/**
 * Back camera preview (CameraX) that reports the text of every QR code it sees (ML Kit,
 * bundled model). [onQrText] is called on the main thread, possibly many times.
 */
@Composable
fun QrCameraPreview(
    onQrText: (String) -> Unit,
    onCameraError: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val currentOnQrText by rememberUpdatedState(onQrText)
    val currentOnError by rememberUpdatedState(onCameraError)
    val previewView = remember {
        PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER }
    }

    DisposableEffect(lifecycleOwner) {
        val analysisExecutor = Executors.newSingleThreadExecutor()
        val scanner = BarcodeScanning.getClient(
            BarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
                .build(),
        )
        val providerFuture = ProcessCameraProvider.getInstance(context)
        var provider: ProcessCameraProvider? = null
        var disposed = false

        providerFuture.addListener(
            {
                if (disposed) return@addListener
                try {
                    val cameraProvider = providerFuture.get()
                    provider = cameraProvider
                    val preview = Preview.Builder().build()
                    preview.setSurfaceProvider(previewView.surfaceProvider)
                    val analysis = ImageAnalysis.Builder()
                        .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                        .build()
                    analysis.setAnalyzer(analysisExecutor) { image ->
                        analyze(image, scanner) { text -> currentOnQrText(text) }
                    }
                    cameraProvider.unbindAll()
                    cameraProvider.bindToLifecycle(
                        lifecycleOwner,
                        CameraSelector.DEFAULT_BACK_CAMERA,
                        preview,
                        analysis,
                    )
                } catch (e: Exception) {
                    currentOnError()
                }
            },
            ContextCompat.getMainExecutor(context),
        )

        onDispose {
            disposed = true
            provider?.unbindAll()
            scanner.close()
            analysisExecutor.shutdown()
        }
    }

    AndroidView(factory = { previewView }, modifier = modifier)
}

@androidx.annotation.OptIn(ExperimentalGetImage::class)
private fun analyze(image: ImageProxy, scanner: BarcodeScanner, onText: (String) -> Unit) {
    val mediaImage = image.image
    if (mediaImage == null) {
        image.close()
        return
    }
    val input = InputImage.fromMediaImage(mediaImage, image.imageInfo.rotationDegrees)
    scanner.process(input)
        .addOnSuccessListener { barcodes ->
            val text = barcodes.firstNotNullOfOrNull { it.rawValue }
            if (text != null) onText(text)
        }
        .addOnCompleteListener { image.close() }
}
