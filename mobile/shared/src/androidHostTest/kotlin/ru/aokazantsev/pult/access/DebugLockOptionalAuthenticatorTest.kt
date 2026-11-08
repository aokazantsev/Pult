package ru.aokazantsev.pult.access

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class DebugLockOptionalAuthenticatorTest {

    private class FakeDevice(private val secure: Boolean) : DeviceAuthenticator {
        var prompts = 0

        override fun isDeviceSecure(): Boolean = secure

        override fun authenticate(onResult: (AuthResult) -> Unit) {
            prompts++
            onResult(AuthResult.Failed)
        }
    }

    @Test
    fun deviceWithoutLockIsTreatedAsSecureAndUnlocksWithoutPrompt() {
        val device = FakeDevice(secure = false)
        val authenticator = DebugLockOptionalAuthenticator(device)
        var result: AuthResult? = null

        authenticator.authenticate { result = it }

        assertTrue(authenticator.isDeviceSecure())
        assertEquals(AuthResult.Success, result)
        assertEquals(0, device.prompts)
    }

    @Test
    fun deviceWithLockStillAsksForPrompt() {
        val device = FakeDevice(secure = true)
        var result: AuthResult? = null

        DebugLockOptionalAuthenticator(device).authenticate { result = it }

        assertEquals(1, device.prompts)
        assertEquals(AuthResult.Failed, result)
    }

    @Test
    fun gateUnlocksOnDeviceWithoutLock() {
        val gate = AccessGate(ElapsedClock { 0L })
        val authenticator = DebugLockOptionalAuthenticator(FakeDevice(secure = false))

        gate.onStart(authenticator.isDeviceSecure())
        authenticator.authenticate { if (it == AuthResult.Success) gate.onAuthenticated() }

        assertEquals(AccessState.Unlocked, gate.state.value)
    }
}
