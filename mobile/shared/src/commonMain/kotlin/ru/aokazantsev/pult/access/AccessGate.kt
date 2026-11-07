package ru.aokazantsev.pult.access

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

class AccessGate(
    private val clock: ElapsedClock,
    private val relockAfterMs: Long = RELOCK_AFTER_MS,
) {

    private var unlockedOnce = false
    private var stoppedAtMs: Long? = null
    private var foreground = false

    private val mutableState = MutableStateFlow(AccessState.Locked)
    val state: StateFlow<AccessState> = mutableState

    private val mutableSessionActive = MutableStateFlow(false)
    val sessionActive: StateFlow<Boolean> = mutableSessionActive

    fun onStart(deviceSecure: Boolean) {
        foreground = true
        if (!deviceSecure) {
            unlockedOnce = false
            stoppedAtMs = null
            mutableState.value = AccessState.NoSecureLock
        } else if (mutableState.value == AccessState.NoSecureLock) {
            unlockedOnce = false
            mutableState.value = AccessState.Locked
        } else if (unlockedOnce && stoppedAtMs.let { it != null && clock.elapsedMs() - it > relockAfterMs }) {
            unlockedOnce = false
            mutableState.value = AccessState.Locked
        }
        stoppedAtMs = null
        refreshSession()
    }

    fun onStop() {
        foreground = false
        if (unlockedOnce && stoppedAtMs == null) stoppedAtMs = clock.elapsedMs()
        refreshSession()
    }

    fun onAuthenticated() {
        if (mutableState.value == AccessState.NoSecureLock) return
        unlockedOnce = true
        stoppedAtMs = if (foreground) null else clock.elapsedMs()
        mutableState.value = AccessState.Unlocked
        refreshSession()
    }

    private fun refreshSession() {
        mutableSessionActive.value = foreground && mutableState.value == AccessState.Unlocked
    }

    companion object {
        const val RELOCK_AFTER_MS = 60_000L
    }
}
