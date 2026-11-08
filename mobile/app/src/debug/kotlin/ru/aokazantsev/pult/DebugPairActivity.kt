package ru.aokazantsev.pult

import android.app.Activity
import android.os.Bundle
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import ru.aokazantsev.pult.di.AppGraphHolder
import ru.aokazantsev.pult.pairing.PairingProgress

class DebugPairActivity : Activity() {

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        start()
        packageManager.getLaunchIntentForPackage(packageName)?.let(::startActivity)
        finish()
    }

    private fun start() {
        val qrText = DebugQrExtra.decode(intent?.getStringExtra(DebugQrExtra.NAME))
        if (qrText == null) {
            Log.w(TAG, "no QR: extra ${DebugQrExtra.NAME} is missing or not base64url")
            return
        }
        val graph = (application as AppGraphHolder).graph
        val existing = graph.pairStore.pair.value
        if (existing != null) {
            Log.w(TAG, "failed: the phone is already paired (${existing.pair}); run pm clear first")
            return
        }
        if (graph.pairingController.progress.value is PairingProgress.Working) {
            Log.w(TAG, "failed: the previous attempt is still running; wait for its result")
            return
        }
        graph.pairingController.dismiss()
        val log = DebugPairingLog(graph.pairingController.progress, graph.pairStore.pair) { Log.i(TAG, it) }
        CoroutineScope(SupervisorJob() + Dispatchers.Default).launch { log.follow() }
        graph.pairingController.submit(qrText)
    }

    private companion object {
        const val TAG = "PultDebugPair"
    }
}
