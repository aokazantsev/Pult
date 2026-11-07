package ru.aokazantsev.pult.access

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.Inject
import dev.zacsweers.metro.SingleIn
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import ru.aokazantsev.pult.session.RelaySession

@SingleIn(AppScope::class)
@Inject
class AppAccess(
    clock: ElapsedClock,
    private val authenticator: DeviceAuthenticator,
    session: RelaySession,
) {

    private val gate = AccessGate(clock)
    private var promptActive = false
    private var autoPromptSuppressed = false
    private var foreground = false

    val state: StateFlow<AccessState> = gate.state

    init {
        CoroutineScope(SupervisorJob() + Dispatchers.Default).launch {
            gate.sessionActive.collect { active -> if (active) session.start() else session.stop() }
        }
    }

    fun onForeground() {
        foreground = true
        gate.onStart(authenticator.isDeviceSecure())
    }

    fun onBackground() {
        foreground = false
        autoPromptSuppressed = false
        gate.onStop()
    }

    fun onResumed() {
        if (!autoPromptSuppressed) authenticate()
    }

    fun requestAuthentication() {
        autoPromptSuppressed = false
        authenticate()
    }

    private fun authenticate() {
        if (gate.state.value != AccessState.Locked || promptActive) return
        promptActive = true
        authenticator.authenticate { result ->
            promptActive = false
            if (result == AuthResult.Success) {
                autoPromptSuppressed = false
                gate.onAuthenticated()
            } else {
                autoPromptSuppressed = foreground
            }
        }
    }
}
