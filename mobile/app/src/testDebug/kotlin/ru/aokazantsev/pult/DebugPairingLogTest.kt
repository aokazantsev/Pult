package ru.aokazantsev.pult

import java.util.Base64
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import ru.aokazantsev.pult.pairing.PairingProgress
import ru.aokazantsev.pult.pairing.StoredPair
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

@OptIn(ExperimentalCoroutinesApi::class)
class DebugPairingLogTest {

    private val progress = MutableStateFlow<PairingProgress>(PairingProgress.Idle)
    private val pair = MutableStateFlow<StoredPair?>(null)
    private val lines = mutableListOf<String>()

    @Test
    fun logsStepsUntilHello() = runTest {
        val job = launch { DebugPairingLog(progress, pair, FOLLOW_MS) { lines += it }.follow() }
        runCurrent()
        progress.value = PairingProgress.Working("Разбор QR")
        runCurrent()
        progress.value = PairingProgress.Working("Отправка ключей на сервер")
        runCurrent()
        pair.value = stored(confirmed = false)
        progress.value = PairingProgress.Idle
        runCurrent()
        pair.value = stored(confirmed = true)
        runCurrent()
        assertTrue(job.isCompleted)
        assertEquals(
            listOf(
                "step: Разбор QR",
                "step: Отправка ключей на сервер",
                "keys sent, waiting for hello from the PC, pair p1",
                "paired: hello from the PC received, pair p1",
            ),
            lines,
        )
    }

    @Test
    fun stopsOnFailure() = runTest {
        val job = launch { DebugPairingLog(progress, pair, FOLLOW_MS) { lines += it }.follow() }
        runCurrent()
        progress.value = PairingProgress.Failed("QR уже использован. Нужен новый QR на ПК")
        runCurrent()
        assertTrue(job.isCompleted)
        assertEquals(listOf("failed: QR уже использован. Нужен новый QR на ПК"), lines)
    }

    @Test
    fun staleFailureEndsFollowingAtOnce() = runTest {
        progress.value = PairingProgress.Failed("QR устарел")
        val job = launch { DebugPairingLog(progress, pair, FOLLOW_MS) { lines += it }.follow() }
        runCurrent()
        assertTrue(job.isCompleted)
        assertEquals(listOf("failed: QR устарел"), lines)
    }

    @Test
    fun reportsPairGoneBeforeHello() = runTest {
        val job = launch { DebugPairingLog(progress, pair, FOLLOW_MS) { lines += it }.follow() }
        runCurrent()
        pair.value = stored(confirmed = false)
        runCurrent()
        pair.value = null
        runCurrent()
        assertTrue(job.isCompleted)
        assertEquals("failed: the pair is gone before hello", lines.last())
    }

    @Test
    fun reportsTimeout() = runTest {
        val job = launch { DebugPairingLog(progress, pair, FOLLOW_MS) { lines += it }.follow() }
        runCurrent()
        pair.value = stored(confirmed = false)
        advanceTimeBy(FOLLOW_MS + 1)
        runCurrent()
        assertTrue(job.isCompleted)
        assertEquals("no result in 150 s", lines.last())
    }

    @Test
    fun decodesBase64UrlExtra() {
        assertEquals("{\"v\":[2]}", DebugQrExtra.decode("eyJ2IjpbMl19"))
        assertEquals(null, DebugQrExtra.decode("not base64!"))
        assertEquals(null, DebugQrExtra.decode(null))
        assertEquals(null, DebugQrExtra.decode(""))
    }

    @Test
    fun decodesExtraBuiltByTheInstruction() {
        assertEquals("<<??>>", DebugQrExtra.decode("PDw_Pz4-"))
        assertEquals(null, DebugQrExtra.decode("PDw/Pz4+"))
        listOf("{\"v\":[2]}x", "{\"v\":[2]}xy").forEach { text ->
            val unpadded = Base64.getUrlEncoder().withoutPadding().encodeToString(text.encodeToByteArray())
            assertEquals(text, DebugQrExtra.decode(unpadded))
        }
    }

    private fun stored(confirmed: Boolean) = StoredPair(
        server = "https://example.com/pult",
        pair = "p1",
        pcSign = "a",
        pcAgree = "b",
        phoneSign = "c",
        phoneAgree = "d",
        idleSeconds = 14400,
        confirmed = confirmed,
        helloDeadlineEpochMs = 0,
        softwareKeys = false,
    )

    private companion object {
        const val FOLLOW_MS = 150_000L
    }
}
