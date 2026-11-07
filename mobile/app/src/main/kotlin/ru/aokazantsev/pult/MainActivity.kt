package ru.aokazantsev.pult

import android.content.Intent
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.os.Build
import android.os.Bundle
import android.util.Base64
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import ru.aokazantsev.pult.app.PultApp
import ru.aokazantsev.pult.di.AppGraphHolder
import ru.aokazantsev.pult.scanner.AndroidQrScanner
import ru.aokazantsev.pult.ui.render.AndroidLinkActions

class MainActivity : ComponentActivity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        hideFromRecents()
        enableEdgeToEdge()
        val graph = (application as AppGraphHolder).graph
        injectDebugQr(intent)
        setContent {
            PultApp(
                graph = graph,
                qrScanner = { modifier, onScanned -> AndroidQrScanner(modifier, onScanned) },
                linkActions = AndroidLinkActions(this),
                onThemeResolved = ::applySystemBars,
                onBackgroundResolved = { window.setBackgroundDrawable(ColorDrawable(it)) },
            )
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        injectDebugQr(intent)
    }

    private fun injectDebugQr(intent: Intent?) {
        if (!BuildConfig.DEBUG) return
        val encoded = intent?.getStringExtra(DEBUG_QR_EXTRA) ?: return
        val text = Base64.decode(encoded, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP).decodeToString()
        (application as AppGraphHolder).graph.pairingController.submit(text)
    }

    private fun hideFromRecents() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            setRecentsScreenshotEnabled(false)
        } else {
            window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        }
    }

    private fun applySystemBars(darkTheme: Boolean) {
        val style = if (darkTheme) {
            SystemBarStyle.dark(Color.TRANSPARENT)
        } else {
            SystemBarStyle.light(Color.TRANSPARENT, Color.TRANSPARENT)
        }
        enableEdgeToEdge(statusBarStyle = style, navigationBarStyle = style)
    }

    private companion object {
        const val DEBUG_QR_EXTRA = "pult_qr_b64"
    }
}
