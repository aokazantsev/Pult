package ru.aokazantsev.pult.access

import kotlin.test.Test
import kotlin.test.assertEquals

class AccessGateTest {

    private var now = 0L
    private val gate = AccessGate(ElapsedClock { now })

    @Test
    fun coldStartIsLockedWithoutSession() {
        gate.onStart(deviceSecure = true)
        assertEquals(AccessState.Locked, gate.state.value)
        assertEquals(false, gate.sessionActive.value)
    }

    @Test
    fun sessionOpensOnlyAfterAuthentication() {
        gate.onStart(true)
        gate.onAuthenticated()
        assertEquals(AccessState.Unlocked, gate.state.value)
        assertEquals(true, gate.sessionActive.value)
    }

    @Test
    fun returnWithinSixtySecondsKeepsAccess() {
        gate.onStart(true)
        gate.onAuthenticated()
        gate.onStop()
        assertEquals(false, gate.sessionActive.value)
        now = 60_000
        gate.onStart(true)
        assertEquals(AccessState.Unlocked, gate.state.value)
        assertEquals(true, gate.sessionActive.value)
    }

    @Test
    fun returnAfterMoreThanSixtySecondsAsksAgain() {
        gate.onStart(true)
        gate.onAuthenticated()
        gate.onStop()
        now = 60_001
        gate.onStart(true)
        assertEquals(AccessState.Locked, gate.state.value)
        assertEquals(false, gate.sessionActive.value)
    }

    @Test
    fun interruptionDuringAuthenticationDoesNotRelock() {
        gate.onStart(true)
        gate.onStop()
        now = 120_000
        gate.onStart(true)
        gate.onAuthenticated()
        gate.onStop()
        now = 130_000
        gate.onStart(true)
        assertEquals(AccessState.Unlocked, gate.state.value)
    }

    @Test
    fun authenticationFinishedInBackgroundStartsRelockTimer() {
        gate.onStart(true)
        gate.onStop()
        gate.onAuthenticated()
        assertEquals(false, gate.sessionActive.value)
        now = 60_001
        gate.onStart(true)
        assertEquals(AccessState.Locked, gate.state.value)
    }

    @Test
    fun authenticationFinishedInBackgroundKeepsAccessWithinThreshold() {
        gate.onStart(true)
        gate.onStop()
        gate.onAuthenticated()
        now = 60_000
        gate.onStart(true)
        assertEquals(AccessState.Unlocked, gate.state.value)
        assertEquals(true, gate.sessionActive.value)
    }

    @Test
    fun withoutSecureLockNothingOpens() {
        gate.onStart(deviceSecure = false)
        gate.onAuthenticated()
        assertEquals(AccessState.NoSecureLock, gate.state.value)
        assertEquals(false, gate.sessionActive.value)
    }

    @Test
    fun restoredSecureLockRequiresNewAuthentication() {
        gate.onStart(true)
        gate.onAuthenticated()
        gate.onStop()
        gate.onStart(false)
        gate.onStop()
        gate.onStart(true)
        assertEquals(AccessState.Locked, gate.state.value)
    }
}
