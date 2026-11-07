package ru.aokazantsev.pult.session

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.Inject
import dev.zacsweers.metro.SingleIn
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChangedBy
import kotlinx.coroutines.flow.filterNotNull
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.joinAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.crypto.DeviceKeyStore
import ru.aokazantsev.pult.crypto.PultCrypto
import ru.aokazantsev.pult.pairing.PairInfo
import ru.aokazantsev.pult.pairing.PairStore
import ru.aokazantsev.pult.pairing.PairingMath
import ru.aokazantsev.pult.pairing.ProtocolCompatibility
import ru.aokazantsev.pult.pairing.StoredPair
import ru.aokazantsev.pult.protocol.Base64Url
import ru.aokazantsev.pult.protocol.EnvelopeCodec
import ru.aokazantsev.pult.protocol.InboundMessage
import ru.aokazantsev.pult.protocol.InboundParser
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.OpenResult
import ru.aokazantsev.pult.protocol.OutboundMessages
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.protocol.Wire
import ru.aokazantsev.pult.relay.RelayApi
import ru.aokazantsev.pult.relay.RelayException
import ru.aokazantsev.pult.relay.RelayResponse
import kotlin.time.Clock

@OptIn(ExperimentalCoroutinesApi::class)
@SingleIn(AppScope::class)
@Inject
class RelaySession(
    private val api: RelayApi,
    private val store: PairStore,
    private val keyStore: DeviceKeyStore,
    private val crypto: PultCrypto,
    dispatcher: CoroutineDispatcher = Dispatchers.Default,
) {

    private val scope = CoroutineScope(SupervisorJob() + dispatcher.limitedParallelism(1))
    private val lifecycleLock = Mutex()
    private val queue = CardQueue()
    private val inFlight = LinkedHashSet<Job>()
    private val math = PairingMath(crypto)
    private val codec = EnvelopeCodec(crypto)
    private var connectionJob: Job? = null
    private var infoJob: Job? = null
    private var activePairId: String? = null
    private var link: ActiveLink? = null
    private var endedReason: String? = null
    private val streamContext = MutableStateFlow<StreamContext?>(null)

    private val mutableState = MutableStateFlow(SessionState())
    val state: StateFlow<SessionState> = mutableState

    fun start() {
        scope.launch { lifecycleLock.withLock { startLocked() } }
    }

    fun stop() {
        scope.launch { lifecycleLock.withLock { stopLocked() } }
    }

    fun unpair() {
        scope.launch {
            val current = store.pair.value ?: return@launch
            if (streamContext.value != null || connectForUnpair()) {
                runCatching { withTimeout(UNPAIR_WAIT_MS) { postSigned("delete", USER_DELETE_BODY.encodeToByteArray()) } }
            }
            endPair(current, reason = null)
        }
    }

    private suspend fun connectForUnpair(): Boolean {
        lifecycleLock.withLock { startLocked() }
        return withTimeoutOrNull(UNPAIR_CONNECT_WAIT_MS) { streamContext.filterNotNull().first() } != null
    }

    fun dismissEnded() {
        mutableState.update { if (it.connection is ConnectionState.PairEnded) it.copy(connection = ConnectionState.NotPaired) else it }
    }

    private fun startLocked() {
        if (connectionJob?.isActive == true) return
        infoJob = scope.launch { store.pair.collect { publishPairInfo(it) } }
        connectionJob = scope.launch {
            store.pair.distinctUntilChangedBy { it?.pair }.collectLatest { pair ->
                if (pair == null) {
                    resetFor(null)
                    val reason = endedReason
                    mutableState.update {
                        it.copy(connection = if (reason != null) ConnectionState.PairEnded(reason) else ConnectionState.NotPaired)
                    }
                } else {
                    endedReason = null
                    resetFor(pair.pair)
                    connectLoop(pair.pair)
                }
            }
        }
    }

    private suspend fun stopLocked() {
        val job = connectionJob ?: return
        withTimeoutOrNull(STOP_WAIT_MS) { joinAll(*inFlight.toTypedArray()) }
        job.cancel()
        job.join()
        infoJob?.cancel()
        connectionJob = null
        infoJob = null
        link = null
        streamContext.value = null
        mutableState.update {
            if (it.connection is ConnectionState.NotPaired || it.connection is ConnectionState.PairEnded) it
            else it.copy(connection = ConnectionState.Idle)
        }
    }

    fun answerPermission(key: String, allow: Boolean) =
        answer(key) { OutboundMessages.permissionResponse(it, allow) }

    fun answerChoice(key: String, selected: List<String>) =
        answer(key) { OutboundMessages.choiceResponse(it, selected) }

    fun answerText(key: String, text: String) =
        answer(key) { OutboundMessages.textResponse(it, text) }

    private fun answer(key: String, build: (requestId: String) -> JsonObject) {
        scope.launch {
            val card = queue.cards.firstOrNull { it.key == key } ?: return@launch
            val request = card.message as? InboundMessage.Request ?: return@launch
            send(key, build(request.requestId))
        }
    }

    private fun send(key: String, inner: JsonObject) {
        val active = link ?: return
        queue.markSending(key)
        publish()
        val id = responseId(key, inner)
        tracked {
            try {
                sendEnvelope(active, id = id, ttl = RESPONSE_TTL_SECONDS, inner = inner)
                queue.markSent(key)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (failure: Exception) {
                queue.markFailed(key, describeFailure(failure))
            }
            publish()
        }
    }

    private fun resetFor(pairId: String?) {
        if (pairId != activePairId) {
            queue.clear()
            activePairId = pairId
            link = null
            streamContext.value = null
            mutableState.update { it.copy(cards = emptyList(), dropped = 0, notice = null) }
        }
    }

    private suspend fun connectLoop(pairId: String) {
        val initial = store.pair.value ?: return
        val keys = keyStore.open(pairId)
        if (keys == null) {
            endPair(initial, "Ключи в хранилище Android не найдены. Сопряжение нужно повторить")
            return
        }
        val direction = try {
            val shared = keys.agree(Base64Url.decode(initial.pcAgree) ?: error("ключ ПК"))
            math.deriveKeys(shared, initial.transcript)
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (failure: Exception) {
            endPair(initial, "Ключи не удалось вывести: ${failure.message ?: failure::class.simpleName}")
            return
        }
        link = ActiveLink(initial, keys, direction)
        coroutineScope {
            val watchdog = launch { helloWatchdog(pairId) }
            try {
                while (true) {
                    val current = store.pair.value ?: return@coroutineScope
                    link = ActiveLink(current, keys, direction)
                    mutableState.update { it.copy(connection = ConnectionState.Connecting, notice = null) }
                    try {
                        runStream(link!!)
                        mutableState.update { it.copy(connection = ConnectionState.Disconnected("Поток закрыт сервером")) }
                    } catch (cancelled: CancellationException) {
                        throw cancelled
                    } catch (failure: Exception) {
                        handleStreamFailure(current, failure)
                    }
                    streamContext.value = null
                    delay(RETRY_DELAY_MS)
                }
            } finally {
                watchdog.cancel()
            }
        }
    }

    private suspend fun handleStreamFailure(pair: StoredPair, failure: Exception) {
        if (failure is RelayException) {
            when {
                failure.status == 410 -> {
                    endPair(pair, PairEndReasons.describe(failure.reason, failure.by))
                    return
                }
                failure.status == 404 -> {
                    endPair(pair, "Пара не найдена на сервере. Сопряжение нужно повторить")
                    return
                }
            }
        }
        mutableState.update {
            it.copy(connection = ConnectionState.Disconnected(describeFailure(failure)))
        }
    }

    private suspend fun helloWatchdog(pairId: String) {
        while (true) {
            val current = store.pair.value ?: return
            if (current.pair != pairId || current.confirmed) return
            val remaining = current.helloDeadlineEpochMs - Clock.System.now().toEpochMilliseconds()
            if (remaining <= 0) break
            delay(minOf(remaining, WATCHDOG_STEP_MS))
        }
        val current = store.pair.value ?: return
        if (current.pair != pairId || current.confirmed) return
        if (streamContext.value != null) {
            runCatching { withTimeout(UNPAIR_WAIT_MS) { postSigned("delete", NO_HELLO_DELETE_BODY.encodeToByteArray()) } }
        }
        endPair(current, "Сопряжение не завершено: ПК не ответил. Отсканируйте новый QR")
    }

    private suspend fun runStream(active: ActiveLink) {
        val pair = active.pair
        val infoProblem = fetchInfoProblem(pair.server)
        if (infoProblem != null) {
            mutableState.update { it.copy(connection = ConnectionState.Incompatible(infoProblem)) }
            delay(INCOMPATIBLE_RETRY_MS)
            return
        }
        val challengeResponse = api.post(
            "${pair.server}/v2/pairs/${pair.pair}/challenge",
            body = "{\"from\":\"phone\"}".encodeToByteArray(),
        ).requireSuccess()
        val challenge = StrictJson.parseObjectOrNull(challengeResponse.body)?.let { JsonFields.string(it, "challenge") }
            ?.takeIf(JsonFields::isIdentifier) ?: error("сервер вернул неверный вызов")
        val signature = Base64Url.encode(
            active.keys.sign(Wire.loginString(pair.pair, Wire.FROM_PHONE, challenge).encodeToByteArray()),
        )
        coroutineScope {
            val pinger = launch { pingLoop() }
            api.openStream(
                url = "${pair.server}/v2/pairs/${pair.pair}/stream",
                headers = mapOf("Pult-From" to Wire.FROM_PHONE, "Pult-Challenge" to challenge, "Pult-Sig" to signature),
                onOpen = {},
                onEvent = { event -> handle(active, event) },
            )
            pinger.cancel()
        }
    }

    private suspend fun fetchInfoProblem(server: String): String? {
        val response = api.get("$server/info")
        if (!response.isSuccess) return "Сервер не отвечает как сервер Pult (${response.status})"
        val info = StrictJson.parseObjectOrNull(response.body) ?: return "Сервер не отвечает как сервер Pult"
        val versions = (info["protocol"] as? JsonArray)?.map { JsonFields.integer(it) ?: return "Версии протокола сервера не разобрать" }
            ?: return "Версии протокола сервера не разобрать"
        return ProtocolCompatibility.serverVersionProblem(versions)
    }

    private suspend fun pingLoop() {
        while (true) {
            postQuietly { postSigned("ping", ByteArray(0)).requireSuccess() }
            delay(PING_INTERVAL_MS)
        }
    }

    private suspend fun handle(active: ActiveLink, event: SseEvent) {
        when (event.event) {
            "server" -> handleServerSignal(active, event.data)
            "message" -> handleMessage(active, event.data)
        }
    }

    private fun handleServerSignal(active: ActiveLink, data: String) {
        val signal = StrictJson.parseObjectOrNull(data) ?: return
        when (JsonFields.string(signal, "type")) {
            "session" -> {
                val stream = JsonFields.string(signal, "stream")?.takeIf(JsonFields::isIdentifier) ?: return
                streamContext.value = StreamContext(stream)
                mutableState.update { it.copy(connection = ConnectionState.Connected) }
                if (store.pair.value?.confirmed == true) tracked { sendHello(active) }
            }
            "intrusion" -> mutableState.update {
                it.copy(notice = "Сервер сообщил: чужая попытка входа или подписи. Пара не разорвана")
            }
            "pair_deleted" -> {
                val current = store.pair.value ?: return
                endPair(current, PairEndReasons.describe(JsonFields.string(signal, "reason"), JsonFields.string(signal, "by")))
            }
        }
    }

    private suspend fun handleMessage(active: ActiveLink, data: String) {
        when (val opened = codec.open(data, active.pair.pair, Wire.FROM_PHONE, active.direction)) {
            is OpenResult.Opened -> {
                val envelope = opened.envelope
                val message = InboundParser.parseInner(envelope.inner)
                if (queue.accept(envelope)) {
                    if (message is InboundMessage.Hello) onHello(active, message)
                    publish()
                }
                tracked { postQuietly { ack(envelope.id) } }
            }
            is OpenResult.Incompatible -> {
                mutableState.update {
                    it.copy(connection = ConnectionState.Incompatible("Версия протокола ПК (${opened.version}) несовместима с телефоном"))
                }
                extractId(data)?.let { id -> tracked { postQuietly { ack(id) } } }
            }
            is OpenResult.Invalid -> {
                mutableState.update { it.copy(dropped = it.dropped + 1) }
                extractId(data)?.let { id -> tracked { postQuietly { ack(id) } } }
            }
        }
    }

    private fun extractId(data: String): String? =
        StrictJson.parseObjectOrNull(data)?.let { JsonFields.string(it, "id") }?.takeIf(JsonFields::isIdentifier)

    private fun onHello(active: ActiveLink, hello: InboundMessage.Hello) {
        if (Wire.VERSION !in hello.protocol) {
            mutableState.update {
                it.copy(connection = ConnectionState.Incompatible(ProtocolCompatibility.pcVersionProblem(hello.protocol).orEmpty()))
            }
            return
        }
        mutableState.update {
            if (it.connection is ConnectionState.Incompatible) it.copy(connection = ConnectionState.Connected) else it
        }
        val current = store.pair.value ?: return
        if (!current.confirmed) {
            store.save(current.copy(confirmed = true))
            tracked { sendHello(active) }
        }
    }

    private suspend fun sendHello(active: ActiveLink) {
        postQuietly { sendEnvelope(active, newId("ph"), HELLO_TTL_SECONDS, OutboundMessages.hello()) }
    }

    private suspend fun ack(id: String) {
        postSigned("acks", "{\"ids\":[\"$id\"]}".encodeToByteArray()).requireSuccess()
    }

    private suspend fun sendEnvelope(active: ActiveLink, id: String, ttl: Int, inner: JsonObject) {
        val body = codec.seal(
            key = active.direction.phoneToPc,
            pair = active.pair.pair,
            from = Wire.FROM_PHONE,
            id = id,
            ts = Clock.System.now().epochSeconds,
            ttl = ttl,
            inner = inner,
        ).encodeToByteArray()
        var attempt = 0
        while (true) {
            val staleId = streamContext.value?.id
            try {
                postSigned("messages", body).requireSuccess()
                return
            } catch (failure: RelayException) {
                if (failure.code == "stale_stream" && attempt++ < STALE_RETRIES) {
                    withTimeout(STREAM_WAIT_MS) { streamContext.first { it != null && it.id != staleId } }
                    continue
                }
                if (failure.code == "unsupported_version") {
                    mutableState.update {
                        it.copy(connection = ConnectionState.Incompatible("Сервер не принимает версию протокола телефона"))
                    }
                }
                throw failure
            }
        }
    }

    private suspend fun postSigned(op: String, body: ByteArray): RelayResponse {
        val active = link ?: error("нет сопряжения")
        val context = withTimeout(STREAM_WAIT_MS) { streamContext.filterNotNull().first() }
        val counter = context.nextCounter()
        val bodyHash = Base64Url.encode(crypto.sha256(body))
        val signature = Base64Url.encode(
            active.keys.sign(
                Wire.postString(op, active.pair.pair, Wire.FROM_PHONE, context.id, counter, bodyHash).encodeToByteArray(),
            ),
        )
        return api.post(
            url = "${active.pair.server}/v2/pairs/${active.pair.pair}/$op",
            headers = mapOf(
                "Pult-From" to Wire.FROM_PHONE,
                "Pult-Stream" to context.id,
                "Pult-N" to counter.toString(),
                "Pult-Sig" to signature,
            ),
            body = body,
        )
    }

    private suspend fun postQuietly(block: suspend () -> Unit) {
        try {
            block()
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (ignored: Exception) {
            return
        }
    }

    private fun endPair(pair: StoredPair, reason: String?) {
        if (store.pair.value?.pair != pair.pair) return
        endedReason = reason
        runCatching { keyStore.delete(pair.pair) }
        store.clear()
    }

    private fun publishPairInfo(pair: StoredPair?) {
        val info = pair?.let {
            PairInfo(
                server = it.server,
                fingerprint = math.fingerprint(it.transcript),
                confirmed = it.confirmed,
                helloDeadlineEpochMs = it.helloDeadlineEpochMs,
                softwareKeys = it.softwareKeys,
                idleSeconds = it.idleSeconds,
            )
        }
        mutableState.update { it.copy(pairInfo = info) }
    }

    private fun describeFailure(failure: Exception): String = when {
        failure is RelayException && failure.code == "not_paired" -> "Пара ещё не активна"
        failure is RelayException && failure.code != null -> "Сервер ответил ${failure.status}: ${failure.code}"
        else -> failure.message ?: failure::class.simpleName.orEmpty()
    }

    private fun tracked(block: suspend () -> Unit) {
        lateinit var job: Job
        job = scope.launch(start = CoroutineStart.LAZY) {
            try {
                block()
            } finally {
                inFlight.remove(job)
            }
        }
        inFlight.add(job)
        job.start()
    }

    private fun publish() {
        mutableState.update { it.copy(cards = queue.cards) }
    }

    private fun responseId(key: String, inner: JsonObject): String {
        val digest = crypto.sha256("$key\n$inner".encodeToByteArray())
        return "r-${Base64Url.encode(digest.copyOf(ID_DIGEST_BYTES))}"
    }

    private fun newId(prefix: String): String = "$prefix-${Base64Url.encode(crypto.randomBytes(ID_RANDOM_BYTES))}"

    private companion object {
        const val PING_INTERVAL_MS = 15_000L
        const val RETRY_DELAY_MS = 2_000L
        const val INCOMPATIBLE_RETRY_MS = 30_000L
        const val STOP_WAIT_MS = 2_000L
        const val UNPAIR_WAIT_MS = 3_000L
        const val UNPAIR_CONNECT_WAIT_MS = 8_000L
        const val STREAM_WAIT_MS = 20_000L
        const val WATCHDOG_STEP_MS = 1_000L
        const val RESPONSE_TTL_SECONDS = 120
        const val HELLO_TTL_SECONDS = 120
        const val STALE_RETRIES = 3
        const val ID_DIGEST_BYTES = 12
        const val ID_RANDOM_BYTES = 12
        const val USER_DELETE_BODY = "{\"reason\":\"user\"}"
        const val NO_HELLO_DELETE_BODY = "{\"reason\":\"no_hello\"}"
    }
}
