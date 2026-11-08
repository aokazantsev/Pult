package ru.aokazantsev.pult

import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeoutOrNull
import ru.aokazantsev.pult.pairing.PairingProgress
import ru.aokazantsev.pult.pairing.StoredPair

class DebugPairingLog(
    private val progress: StateFlow<PairingProgress>,
    private val pair: StateFlow<StoredPair?>,
    private val followMs: Long = FOLLOW_MS,
    private val log: (String) -> Unit,
) {

    suspend fun follow() {
        var last: String? = null
        var sawPair = false
        val finished = withTimeoutOrNull(followMs) {
            combine(progress, pair) { current, stored -> current to stored }.first { (current, stored) ->
                val line = describe(current, stored, sawPair)
                if (line != null && line != last) {
                    log(line)
                    last = line
                }
                if (stored != null) sawPair = true
                current is PairingProgress.Failed || stored?.confirmed == true || (sawPair && stored == null)
            }
        }
        if (finished == null) log("no result in ${followMs / 1000} s")
    }

    private fun describe(current: PairingProgress, stored: StoredPair?, sawPair: Boolean): String? = when {
        current is PairingProgress.Failed -> "failed: ${current.reason}"
        current is PairingProgress.Working -> "step: ${current.step}"
        stored?.confirmed == true -> "paired: hello from the PC received, pair ${stored.pair}"
        stored != null -> "keys sent, waiting for hello from the PC, pair ${stored.pair}"
        sawPair -> "failed: the pair is gone before hello"
        else -> null
    }

    private companion object {
        const val FOLLOW_MS = 150_000L
    }
}
