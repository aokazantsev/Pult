package ru.aokazantsev.pult.access

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.test.StandardTestDispatcher
import ru.aokazantsev.pult.crypto.CreatedKeys
import ru.aokazantsev.pult.crypto.DeviceKeyStore
import ru.aokazantsev.pult.crypto.DeviceKeys
import ru.aokazantsev.pult.crypto.JcaCrypto
import ru.aokazantsev.pult.pairing.PairStore
import ru.aokazantsev.pult.pairing.StoredPair
import ru.aokazantsev.pult.session.FakeRelayApi
import ru.aokazantsev.pult.session.RelaySession

@OptIn(ExperimentalCoroutinesApi::class)
class AppAccessTest {

    private class CountingAuthenticator : DeviceAuthenticator {
        var calls = 0
        var pending: ((AuthResult) -> Unit)? = null

        override fun isDeviceSecure(): Boolean = true

        override fun authenticate(onResult: (AuthResult) -> Unit) {
            calls++
            pending = onResult
        }

        fun finish(result: AuthResult) {
            pending?.invoke(result)
            pending = null
        }
    }

    private object EmptyPairStore : PairStore {
        override val pair: StateFlow<StoredPair?> = MutableStateFlow(null)

        override fun save(pair: StoredPair) = Unit

        override fun clear() = Unit
    }

    private object NoKeys : DeviceKeyStore {
        override fun create(pair: String): CreatedKeys = error("не нужен")

        override fun open(pair: String): DeviceKeys? = null

        override fun delete(pair: String) = Unit

        override fun deleteAllExcept(pair: String?) = Unit
    }

    private val authenticator = CountingAuthenticator()
    private val access = AppAccess(
        clock = ElapsedClock { 0L },
        authenticator = authenticator,
        session = RelaySession(FakeRelayApi(), EmptyPairStore, NoKeys, JcaCrypto(), ElapsedClock { 0L }, StandardTestDispatcher()),
    )

    @Test
    fun cancelledPromptIsNotRepeatedOnResumeButButtonAsksAgain() {
        access.onForeground()
        access.onResumed()
        assertEquals(1, authenticator.calls)
        authenticator.finish(AuthResult.Failed)
        access.onResumed()
        assertEquals(1, authenticator.calls)
        access.requestAuthentication()
        assertEquals(2, authenticator.calls)
    }

    @Test
    fun promptReturnsAfterLeavingTheScreen() {
        access.onForeground()
        access.onResumed()
        authenticator.finish(AuthResult.Failed)
        access.onBackground()
        access.onForeground()
        access.onResumed()
        assertEquals(2, authenticator.calls)
        authenticator.finish(AuthResult.Success)
        assertEquals(AccessState.Unlocked, access.state.value)
    }

    @Test
    fun systemCancelAfterLeavingDoesNotSuppressPromptOnReturn() {
        access.onForeground()
        access.onResumed()
        access.onBackground()
        authenticator.finish(AuthResult.Failed)
        access.onForeground()
        access.onResumed()
        assertEquals(2, authenticator.calls)
    }

    @Test
    fun secondPromptIsNotStartedWhileOneIsOpen() {
        access.onForeground()
        access.onResumed()
        access.onResumed()
        access.requestAuthentication()
        assertEquals(1, authenticator.calls)
    }
}
