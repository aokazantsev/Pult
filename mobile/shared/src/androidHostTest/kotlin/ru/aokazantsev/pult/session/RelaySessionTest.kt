package ru.aokazantsev.pult.session

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.test.StandardTestDispatcher
import kotlinx.coroutines.test.TestScope
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.access.ElapsedClock
import ru.aokazantsev.pult.crypto.CreatedKeys
import ru.aokazantsev.pult.crypto.DeviceKeyStore
import ru.aokazantsev.pult.crypto.DeviceKeys
import ru.aokazantsev.pult.crypto.JcaCrypto
import ru.aokazantsev.pult.pairing.PairStore
import ru.aokazantsev.pult.pairing.PairingMath
import ru.aokazantsev.pult.pairing.StoredPair
import ru.aokazantsev.pult.protocol.Base64Url
import ru.aokazantsev.pult.protocol.DirectionKeys
import ru.aokazantsev.pult.protocol.EnvelopeCodec
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.OpenResult
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.relay.RelayResponse
import ru.aokazantsev.pult.vectors.FixedKeys

@OptIn(ExperimentalCoroutinesApi::class)
class RelaySessionTest {

    private class MemoryPairStore(initial: StoredPair?) : PairStore {
        private val flow = MutableStateFlow(initial)
        override val pair: StateFlow<StoredPair?> = flow

        override fun save(pair: StoredPair) {
            flow.value = pair
        }

        override fun clear() {
            flow.value = null
        }
    }

    private class FixedKeyStore(private val keys: DeviceKeys) : DeviceKeyStore {
        var deleted = 0

        override fun create(pair: String): CreatedKeys = error("не нужен")

        override fun open(pair: String): DeviceKeys = keys

        override fun delete(pair: String) {
            deleted++
        }

        override fun deleteAllExcept(pair: String?) = Unit
    }

    private val crypto = JcaCrypto()
    private val codec = EnvelopeCodec(crypto)
    private val math = PairingMath(crypto)
    private val phoneKeys = FixedKeys.phone()
    private val pair = StoredPair(
        server = "https://relay.example",
        pair = "p1",
        pcSign = FixedKeys.publicOf("pc_sign"),
        pcAgree = FixedKeys.publicOf("pc_agree"),
        phoneSign = FixedKeys.publicOf("phone_sign"),
        phoneAgree = FixedKeys.publicOf("phone_agree"),
        idleSeconds = 14400,
        confirmed = true,
        helloDeadlineEpochMs = 0,
        softwareKeys = false,
    )
    private val direction: DirectionKeys = math.deriveKeys(
        phoneKeys.agree(Base64Url.decode(pair.pcAgree)!!),
        pair.transcript,
    )

    private val api = FakeRelayApi()
    private val keyStore = FixedKeyStore(phoneKeys)
    private var counter = 0

    private lateinit var session: RelaySession

    private fun TestScope.newSession(store: PairStore): RelaySession =
        RelaySession(api, store, keyStore, crypto, ElapsedClock { testScheduler.currentTime }, StandardTestDispatcher(testScheduler)).also { session = it }

    private fun runSessionTest(block: suspend TestScope.() -> Unit) = runTest {
        try {
            block()
        } finally {
            if (this@RelaySessionTest::session.isInitialized) {
                session.stop()
                runCurrent()
                advanceTimeBy(STOP_SETTLE_MS)
                runCurrent()
            }
        }
    }

    private fun pcMessage(inner: String): SseEvent {
        val envelope = codec.seal(
            key = direction.pcToPhone,
            pair = pair.pair,
            from = "pc",
            id = "pc-${++counter}",
            ts = 1_793_989_146,
            ttl = 0,
            inner = StrictJson.parseObject(inner),
        )
        return SseEvent(null, "message", envelope)
    }

    private fun sentByPhone(): List<JsonObject> = api.urls("/messages").map { call ->
        val opened = codec.open(call.body.decodeToString(), pair.pair, ownSide = "pc", keys = direction)
        assertIs<OpenResult.Opened>(opened).envelope.inner
    }

    private fun envelopeTtl(call: FakeRelayApi.Call): Long =
        JsonFields.integer(StrictJson.parseObject(call.body.decodeToString())["ttl"])!!

    private suspend fun TestScope.connect(store: PairStore = MemoryPairStore(pair)): MemoryPairStore {
        val memory = store as MemoryPairStore
        newSession(memory).start()
        runCurrent()
        return memory
    }

    @Test
    fun helloCarriesCapabilitiesAndFeedPositions() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"thread","thread":"t1","title":"A","folder":"Pult","status":"working","pc_dialog":false}"""))
        api.emit(pcMessage("""{"type":"feed","thread":"t1","entries":[{"seq":1,"kind":"notice","text":"x"}]}"""))
        runCurrent()
        api.emit(SseEvent(null, "server", "{\"type\":\"session\",\"stream\":\"s2\"}"))
        runCurrent()
        val hello = sentByPhone().last()
        assertEquals("hello", JsonFields.string(hello, "type"))
        assertTrue(hello.toString().contains("\"capabilities\":[\"choice\"]"))
        assertTrue(hello.toString().contains("\"feeds\":[{\"thread\":\"t1\",\"seq\":1}]"))
    }

    @Test
    fun untrustedFeedTextCausesNoRequestsOutsideThePairEndpoints() = runSessionTest {
        connect()
        val feed = """{"type":"feed","thread":"t1","entries":[
            {"seq":1,"kind":"assistant","text":"![x](https://tracker.example/pixel.png) https://evil.example/a [l](https://evil.example/b)"},
            {"seq":2,"kind":"tool_result","call":"c1","output":"http://evil.example/c","error":false}]}"""
        api.emit(pcMessage(feed.replace("\n", "")))
        runCurrent()
        val prefix = "${pair.server}/v2/pairs/${pair.pair}/"
        api.calls.forEach { call ->
            assertTrue(call.url == "${pair.server}/info" || call.url.startsWith(prefix), call.url)
        }
        assertEquals(2, session.state.value.content.slices.getValue("t1").entries.size)
    }

    @Test
    fun snapshotBuildsThreadsAndCards() = runSessionTest {
        connect()
        api.emit(
            pcMessage(
                """{"type":"snapshot","snapshot":"s1","part":1,"last":true,"mode":"pc",
                "threads":[{"thread":"t1","title":"A","folder":"Pult","status":"working","pc_dialog":true,"last_seq":3}],
                "requests":[{"type":"permission_request","request_id":"r1","thread":"t1","title":"A","after_seq":3,"tool":"Bash","summary":"ls"}]}""".replace("\n", ""),
            ),
        )
        runCurrent()
        val content = session.state.value.content
        assertEquals("pc", content.mode?.wire)
        assertEquals(listOf("t1"), content.threads.map { it.info.thread })
        assertEquals(CardState.Open, content.cards.getValue("t1").single().state)
        assertTrue(content.threads.single().marks.awaitsAction)
    }

    @Test
    fun missingSnapshotIsRequestedAgainAfterTenSeconds() = runSessionTest {
        connect()
        runCurrent()
        val before = sentByPhone().count { JsonFields.string(it, "type") == "hello" }
        advanceTimeBy(10_001)
        runCurrent()
        val after = sentByPhone().count { JsonFields.string(it, "type") == "hello" }
        assertEquals(before + 1, after)
    }

    @Test
    fun brokenSnapshotPartRequestsSnapshotAgain() = runSessionTest {
        connect()
        runCurrent()
        val before = sentByPhone().count { JsonFields.string(it, "type") == "hello" }
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s9","part":2,"last":true}"""))
        runCurrent()
        assertEquals(0, session.state.value.dropped)
        assertEquals(before + 1, sentByPhone().count { JsonFields.string(it, "type") == "hello" })
    }

    @Test
    fun malformedMessageIsCountedAndAcknowledged() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"mode","mode":null}"""))
        runCurrent()
        assertEquals(1, session.state.value.dropped)
        assertEquals(1, api.urls("/acks").size)
    }

    @Test
    fun permissionAnswerIsSentWithResponseLifetimeAndMarkedSent() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"permission_request","request_id":"r1","thread":"t1","title":"A","after_seq":0,"tool":"Bash","summary":"ls"}"""))
        runCurrent()
        session.answerPermission("r1", allow = true)
        runCurrent()
        val call = api.urls("/messages").last()
        assertEquals(600L, envelopeTtl(call))
        assertEquals("permission_response", JsonFields.string(sentByPhone().last(), "type"))
        assertEquals(CardState.Sent, session.state.value.content.cards.getValue("t1").single().state)
    }

    @Test
    fun transientFailureIsRetriedWithTheSameEnvelopeId() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"permission_request","request_id":"r1","thread":"t1","title":"A","after_seq":0,"tool":"Bash","summary":"ls"}"""))
        runCurrent()
        api.failMessagePosts = 1
        session.answerPermission("r1", allow = false)
        runCurrent()
        advanceTimeBy(2_001)
        runCurrent()
        val posts = api.urls("/messages").takeLast(2)
        val ids = posts.map { JsonFields.string(StrictJson.parseObject(it.body.decodeToString()), "id") }
        assertEquals(ids[0], ids[1])
        assertEquals(CardState.Sent, session.state.value.content.cards.getValue("t1").single().state)
    }

    @Test
    fun permanentRefusalReturnsCardToOpenWithError() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"permission_request","request_id":"r1","thread":"t1","title":"A","after_seq":0,"tool":"Bash","summary":"ls"}"""))
        runCurrent()
        api.failMessagePosts = 1
        api.failStatus = 400
        session.answerPermission("r1", allow = true)
        runCurrent()
        val card = session.state.value.content.cards.getValue("t1").single()
        assertEquals(CardState.Open, card.state)
        assertNotNull(card.error)
    }

    @Test
    fun pairDeletedSignalErasesSessionAndShowsReason() = runSessionTest {
        val store = connect()
        api.emit(pcMessage("""{"type":"thread","thread":"t1","title":"A","folder":"Pult","status":"done","pc_dialog":false}"""))
        runCurrent()
        api.emit(SseEvent(null, "server", "{\"type\":\"pair_deleted\",\"reason\":\"idle\",\"by\":\"pc\"}"))
        runCurrent()
        val state = session.state.value
        assertNull(store.pair.value)
        assertEquals(emptyList(), state.content.threads)
        assertEquals(
            ConnectionState.PairEnded("Сессия закрыта: нет событий 4 ч"),
            state.connection,
        )
        assertEquals(1, keyStore.deleted)
    }

    @Test
    fun unpairWithClosedStreamStillReachesThePc() = runSessionTest {
        val store = MemoryPairStore(pair)
        newSession(store)
        session.unpair()
        runCurrent()
        assertEquals(1, api.urls("/delete").size)
        assertNull(store.pair.value)
        assertEquals(ConnectionState.PairEnded(PairEndReasons.PHONE_UNPAIRED), session.state.value.connection)
    }


    private fun permissionRequest() =
        pcMessage("""{"type":"permission_request","request_id":"r1","thread":"t1","title":"A","after_seq":0,"tool":"Bash","summary":"ls"}""")

    private fun helloCount(): Int = sentByPhone().count { JsonFields.string(it, "type") == "hello" }

    private fun card(): RequestCard = session.state.value.content.cards.getValue("t1").single()

    @Test
    fun streamTimeoutDuringAnswerReturnsCardToOpenWithError() = runSessionTest {
        connect()
        api.emit(permissionRequest())
        runCurrent()
        api.failStatus = 409
        api.failBody = "{\"error\":\"stale_stream\"}"
        api.failMessagePosts = 100
        session.answerPermission("r1", allow = true)
        runCurrent()
        advanceTimeBy(STREAM_WAIT_MS * 2)
        runCurrent()
        assertEquals(CardState.Sending, card().state)
        advanceTimeBy(STREAM_WAIT_MS * 3 + 1)
        runCurrent()
        assertEquals(CardState.Open, card().state)
        assertEquals("Нет связи с сервером, повторите", card().error)
    }

    @Test
    fun helloIsRepeatedWhenFirstHelloTimesOut() = runSessionTest {
        api.failStatus = 409
        api.failBody = "{\"error\":\"stale_stream\"}"
        api.failMessagePosts = 1
        connect()
        assertEquals(1, helloCount())
        advanceTimeBy(STREAM_WAIT_MS + SNAPSHOT_WAIT_MS + 1)
        runCurrent()
        assertEquals(2, helloCount())
    }

    @Test
    fun transientFailuresStopAfterFiveAttempts() = runSessionTest {
        connect()
        api.emit(permissionRequest())
        runCurrent()
        api.failMessagePosts = 5
        session.answerPermission("r1", allow = true)
        runCurrent()
        advanceTimeBy(4 * DELIVERY_BACKOFF_MS + 1)
        runCurrent()
        val answers = api.urls("/messages").filter {
            val opened = codec.open(it.body.decodeToString(), pair.pair, ownSide = "pc", keys = direction)
            JsonFields.string(assertIs<OpenResult.Opened>(opened).envelope.inner, "type") == "permission_response"
        }
        assertEquals(5, answers.size)
        assertEquals(1, answers.map { JsonFields.string(StrictJson.parseObject(it.body.decodeToString()), "id") }.toSet().size)
        assertEquals(CardState.Open, card().state)
        assertEquals("Сервер ответил 503: busy", card().error)
    }

    @Test
    fun goneOnConnectEndsPairWithReason() = runSessionTest {
        api.challengeResponse = RelayResponse(410, "{\"error\":\"pair_deleted\",\"reason\":\"replaced\",\"by\":\"pc\"}")
        val store = connect()
        assertNull(store.pair.value)
        assertEquals(ConnectionState.PairEnded("На ПК начата новая сессия"), session.state.value.connection)
        assertEquals(1, keyStore.deleted)
    }

    @Test
    fun missingPairOnConnectShowsGoneText() = runSessionTest {
        api.challengeResponse = RelayResponse(404, "{\"error\":\"not_found\"}")
        connect()
        assertEquals(ConnectionState.PairEnded(PairEndReasons.GONE), session.state.value.connection)
    }

    @Test
    fun pcHelloConfirmsPairAndRepliesWithHello() = runSessionTest {
        val store = connect(MemoryPairStore(pair.copy(confirmed = false, helloDeadlineEpochMs = Long.MAX_VALUE / 2)))
        assertEquals(0, helloCount())
        api.emit(pcMessage("""{"type":"hello","protocol":[2],"capabilities":[]}"""))
        runCurrent()
        assertEquals(true, store.pair.value?.confirmed)
        assertEquals("hello", JsonFields.string(sentByPhone().last(), "type"))
    }

    @Test
    fun pcHelloOfUnknownVersionIsIncompatible() = runSessionTest {
        val store = connect(MemoryPairStore(pair.copy(confirmed = false, helloDeadlineEpochMs = Long.MAX_VALUE / 2)))
        api.emit(pcMessage("""{"type":"hello","protocol":[3],"capabilities":[]}"""))
        runCurrent()
        assertEquals(false, store.pair.value?.confirmed)
        assertIs<ConnectionState.Incompatible>(session.state.value.connection)
    }

    @Test
    fun snapshotWithinTenSecondsCancelsRepeatHello() = runSessionTest {
        connect()
        val hello = api.urls("/messages").single()
        assertEquals(120L, envelopeTtl(hello))
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":1,"last":true,"mode":"phone"}"""))
        runCurrent()
        advanceTimeBy(SNAPSHOT_WAIT_MS + 1)
        runCurrent()
        assertEquals(1, helloCount())
    }


    private fun answerPosts(): List<FakeRelayApi.Call> = api.urls("/messages").filter {
        val opened = codec.open(it.body.decodeToString(), pair.pair, ownSide = "pc", keys = direction)
        JsonFields.string(assertIs<OpenResult.Opened>(opened).envelope.inner, "type") == "permission_response"
    }

    private fun envelopeId(call: FakeRelayApi.Call): String? =
        JsonFields.string(StrictJson.parseObject(call.body.decodeToString()), "id")

    private fun brokenPart() = pcMessage("""{"type":"snapshot","snapshot":"s9","part":2,"last":true}""")

    @Test
    fun hangingAnswerPostTimesOutAndReturnsCardToOpen() = runSessionTest {
        connect()
        api.emit(permissionRequest())
        runCurrent()
        api.hangPosts = setOf("/messages")
        session.answerPermission("r1", allow = true)
        runCurrent()
        advanceTimeBy(POST_TIMEOUT_MS * 4)
        runCurrent()
        assertEquals(CardState.Sending, card().state)
        advanceTimeBy(POST_TIMEOUT_MS + 1)
        runCurrent()
        assertEquals(CardState.Open, card().state)
        assertEquals("Нет связи с сервером, повторите", card().error)
        assertEquals(5, answerPosts().size)
        assertEquals(1, answerPosts().map(::envelopeId).toSet().size)
    }

    @Test
    fun stopDuringRetriesStopsDeliveryAndAsksToRepeat() = runSessionTest {
        connect()
        api.emit(permissionRequest())
        runCurrent()
        api.failMessagePosts = 5
        session.answerPermission("r1", allow = true)
        runCurrent()
        session.stop()
        runCurrent()
        advanceTimeBy(5 * DELIVERY_BACKOFF_MS)
        runCurrent()
        assertTrue(answerPosts().size <= 2)
        assertEquals(CardState.Open, card().state)
        assertEquals("Нет связи с сервером, повторите", card().error)
    }

    @Test
    fun retriesSurviveReconnectOfTheSamePair() = runSessionTest {
        connect()
        api.emit(permissionRequest())
        runCurrent()
        api.failMessagePosts = 2
        session.answerPermission("r1", allow = true)
        runCurrent()
        api.closeStream()
        runCurrent()
        advanceTimeBy(3 * DELIVERY_BACKOFF_MS + 1)
        runCurrent()
        assertEquals(CardState.Sent, card().state)
        assertEquals(1, answerPosts().map(::envelopeId).toSet().size)
        assertEquals("s2", answerPosts().last().headers["Pult-Stream"])
    }

    @Test
    fun hungPingTimesOutAndPingingContinues() = runSessionTest {
        api.hangPosts = setOf("/ping")
        connect()
        advanceTimeBy(POST_TIMEOUT_MS + PING_INTERVAL_MS + 1)
        runCurrent()
        assertTrue(api.urls("/ping").size >= 2)
        assertEquals(ConnectionState.Connected, session.state.value.connection)
    }

    @Test
    fun discardedSnapshotCountsAsDroppedOnlyAfterRepeatsAreExhausted() = runSessionTest {
        connect()
        api.emit(brokenPart())
        runCurrent()
        advanceTimeBy(3 * SNAPSHOT_WAIT_MS + 3)
        runCurrent()
        assertEquals(0, session.state.value.dropped)
        assertEquals(4, helloCount())
        api.emit(brokenPart())
        runCurrent()
        assertEquals(1, session.state.value.dropped)
        assertEquals(4, helloCount())
    }

    @Test
    fun secondDiscardWithinTenSecondsStillRequestsSnapshotLater() = runSessionTest {
        connect()
        api.emit(brokenPart())
        runCurrent()
        val afterFirstRepeat = helloCount()
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s10","part":1,"last":false,"mode":"pc"}"""))
        runCurrent()
        advanceTimeBy(2 * DELIVERY_BACKOFF_MS)
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s10","part":3,"last":true}"""))
        runCurrent()
        assertEquals(afterFirstRepeat, helloCount())
        advanceTimeBy(SNAPSHOT_WAIT_MS)
        runCurrent()
        assertEquals(afterFirstRepeat + 1, helloCount())
        assertEquals(0, session.state.value.dropped)
    }

    @Test
    fun incompleteSnapshotIsRequestedAgainWhenNoMorePartsCome() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":1,"last":false,"mode":"pc"}"""))
        runCurrent()
        val before = helloCount()
        advanceTimeBy(SNAPSHOT_WAIT_MS + 1)
        runCurrent()
        assertEquals(before + 1, helloCount())
    }

    @Test
    fun completeSnapshotStopsWaiting() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":1,"last":false,"mode":"pc"}"""))
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":2,"last":true}"""))
        runCurrent()
        val before = helloCount()
        advanceTimeBy(SNAPSHOT_WAIT_MS * 2)
        runCurrent()
        assertEquals(before, helloCount())
        assertEquals(true, session.state.value.content.snapshotApplied)
    }

    @Test
    fun snapshotArrivingDuringHelloPostIsNotRequestedAgain() = runSessionTest {
        api.hangPosts = setOf("/messages")
        connect()
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":1,"last":true,"mode":"pc"}"""))
        runCurrent()
        advanceTimeBy(POST_TIMEOUT_MS + SNAPSHOT_WAIT_MS + 1)
        runCurrent()
        assertEquals(1, helloCount())
        assertEquals(true, session.state.value.content.snapshotApplied)
    }

    @Test
    fun eachSnapshotPartRestartsTheWait() = runSessionTest {
        connect()
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":1,"last":false,"mode":"pc"}"""))
        runCurrent()
        advanceTimeBy(9_000)
        api.emit(pcMessage("""{"type":"snapshot","snapshot":"s1","part":2,"last":false}"""))
        runCurrent()
        val before = helloCount()
        advanceTimeBy(2_000)
        runCurrent()
        assertEquals(before, helloCount())
        advanceTimeBy(8_001)
        runCurrent()
        assertEquals(before + 1, helloCount())
    }

    @Test
    fun answerWithoutLinkReturnsCardWithNoConnection() = runSessionTest {
        connect()
        api.emit(permissionRequest())
        runCurrent()
        session.stop()
        runCurrent()
        advanceTimeBy(2_001)
        runCurrent()
        session.answerPermission("r1", allow = true)
        runCurrent()
        assertEquals(CardState.Open, card().state)
        assertEquals("Нет связи с сервером, повторите", card().error)
        assertEquals(0, answerPosts().size)
    }

    @Test
    fun hungHelloPostIsRepeatedAfterTimeout() = runSessionTest {
        api.hangPosts = setOf("/messages")
        connect()
        advanceTimeBy(POST_TIMEOUT_MS + SNAPSHOT_WAIT_MS + 1)
        runCurrent()
        assertEquals(2, helloCount())
    }

    private companion object {
        const val STOP_SETTLE_MS = 5_000L
        const val POST_TIMEOUT_MS = 15_000L
        const val PING_INTERVAL_MS = 15_000L
        const val STREAM_WAIT_MS = 20_000L
        const val SNAPSHOT_WAIT_MS = 10_000L
        const val DELIVERY_BACKOFF_MS = 2_000L
    }
}
