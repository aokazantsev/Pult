package ru.aokazantsev.pult.scanner

import android.Manifest
import android.content.pm.PackageManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.Preview
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.LocalLifecycleOwner
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicBoolean
import ru.aokazantsev.pult.designsystem.component.PultPrimaryButton
import ru.aokazantsev.pult.designsystem.component.PultText
import ru.aokazantsev.pult.designsystem.theme.PultTheme

@Composable
fun AndroidQrScanner(modifier: Modifier, onScanned: (String) -> Unit) {
    val context = LocalContext.current
    var granted by remember {
        mutableStateOf(ContextCompat.checkSelfPermission(context, Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED)
    }
    var denied by remember { mutableStateOf(false) }
    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        granted = it
        denied = !it
    }
    if (granted) {
        CameraPreview(modifier = modifier, onScanned = onScanned)
    } else {
        Column(
            modifier = modifier.fillMaxSize().padding(PultTheme.spacing.l),
            verticalArrangement = Arrangement.spacedBy(PultTheme.spacing.m, Alignment.CenterVertically),
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            PultText(
                text = if (denied) {
                    "Без доступа к камере QR не прочитать. Разрешите камеру в настройках приложения."
                } else {
                    "Для чтения QR с экрана ПК нужна камера."
                },
                textAlign = TextAlign.Center,
            )
            PultPrimaryButton(text = "Разрешить камеру", onClick = { launcher.launch(Manifest.permission.CAMERA) })
        }
    }
}

@Composable
private fun CameraPreview(modifier: Modifier, onScanned: (String) -> Unit) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val analyzerExecutor = remember { Executors.newSingleThreadExecutor() }
    val delivered = remember { AtomicBoolean(false) }
    val decoder = remember { QrFrameDecoder() }
    val previewView = remember { PreviewView(context).apply { scaleType = PreviewView.ScaleType.FILL_CENTER } }
    DisposableEffect(lifecycleOwner) {
        val providerFuture = ProcessCameraProvider.getInstance(context)
        val mainExecutor = ContextCompat.getMainExecutor(context)
        providerFuture.addListener({
            val provider = providerFuture.get()
            val preview = Preview.Builder().build().also { it.surfaceProvider = previewView.surfaceProvider }
            val analysis = ImageAnalysis.Builder()
                .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                .build()
                .also { useCase ->
                    useCase.setAnalyzer(analyzerExecutor) { image ->
                        try {
                            if (!delivered.get()) {
                                decoder.decode(image)?.let { text ->
                                    if (delivered.compareAndSet(false, true)) mainExecutor.execute { onScanned(text) }
                                }
                            }
                        } finally {
                            image.close()
                        }
                    }
                }
            provider.unbindAll()
            provider.bindToLifecycle(lifecycleOwner, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
        }, mainExecutor)
        onDispose {
            runCatching { providerFuture.get().unbindAll() }
            analyzerExecutor.shutdown()
        }
    }
    AndroidView(factory = { previewView }, modifier = modifier.fillMaxSize())
}
