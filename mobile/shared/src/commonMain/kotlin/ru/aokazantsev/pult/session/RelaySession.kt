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
import kotlinx.coroutines.TimeoutCancellationException
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
import ru.aokazantsev.pult.access.ElapsedClock
import ru.aokazantsev.pult.crypto.DeviceKeyStore
import ru.aokazantsev.pult.crypto.PultCrypto
import ru.aokazantsev.pult.pairing.PairInfo
import ru.aokazantsev.pult.pairing.PairStore
import ru.aokazantsev.pult.pairing.PairingMath
import ru.aokazantsev.pult.pairing.ProtocolCompatibility
import ru.aokazantsev.pult.pairing.StoredPair
import ru.aokazantsev.pult.protocol.Base64Url
import ru.aokazantsev.pult.protocol.EnvelopeCodec
import ru.aokazantsev.pult.protocol.JsonFields
import ru.aokazantsev.pult.protocol.OpenResult
import ru.aokazantsev.pult.protocol.OutboundMessages
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.protocol.message.MessageParser
import ru.aokazantsev.pult.protocol.message.ParseResult
import ru.aokazantsev.pult.protocol.message.PcMessage
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
    private val clock: ElapsedClock,
    dispatcher: CoroutineDispatcher = Dispatchers.Default,
) {

    private val scope = CoroutineScope(SupervisorJob() + dispatcher.limitedParallelism(1))
    private val lifecycleLock = Mutex()
    private val model = SessionModel()
    private val processed = ProcessedIds()
    private val assembler = SnapshotAssembler()
    private val helloPolicy = HelloRequestPolicy()
    private var snapshotWaitToken = 0L
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
            endPair(current, reason = PairEndReasons.PHONE_UNPAIRED)
        }
    }

    private suspend fun connectForUnpair(): Boolean {
        lifecycleLock.withLock { startLocked() }
        return withTimeoutOrNull(UNPAIR_CONNECT_WAIT_MS) { streamContext.filterNotNull().first() } != null
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
        answer(key, text = null) { OutboundMessages.permissionResponse(it, allow) }

    fun answerChoice(key: String, answers: List<ChoiceAnswer>) =
        answer(key, text = null) { OutboundMessages.choiceResponse(it, answers) }

    fun answerText(key: String, text: String) =
        answer(key, text = text) { OutboundMessages.textResponse(it, text) }

    private fun answer(key: String, text: String?, build: (requestId: String) -> JsonObject) {
        scope.launch {
            val card = model.cards[key] ?: return@launch
            if (card.state != CardState.Open || card.request == null) return@launch
            send(key, text, build(card.requestId))
        }
    }

    private fun send(key: String, text: String?, inner: JsonObject) {
        val active = link
        if (active == null) {
            model.markSending(key, text)
            model.markFailed(key, NO_CONNECTION)
            publish()
            return
        }
        model.markSending(key, text)
        publish()
        val id = responseId(key, inner)
        tracked {
            try {
                deliver(active, id = id, ttl = RESPONSE_TTL_SECONDS, inner = inner)
                model.markSent(key)
            } catch (timeout: TimeoutCancellationException) {
                model.markFailed(key, NO_CONNECTION)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (failure: Exception) {
                model.markFailed(key, if (isSamePair(active)) describeFailure(failure) else NO_CONNECTION)
            }
            publish()
        }
    }

    private fun resetFor(pairId: String?) {
        if (pairId != activePairId) {
            clearSessionData()
            activePairId = pairId
            link = null
            streamContext.value = null
            mutableState.update { it.copy(dropped = 0, notice = null) }
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
                    endPair(pair, PairEndReasons.describe(failure.reason, failure.by, pair.idleSeconds))
                    return
                }
                failure.status == 404 -> {
                    endPair(pair, PairEndReasons.GONE)
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
        endPair(current, PairEndReasons.NO_HELLO)
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
                assembler.reset()
                helloPolicy.reset()
                snapshotWaitToken++
                mutableState.update { it.copy(connection = ConnectionState.Connected) }
                if (store.pair.value?.confirmed == true) tracked { sendHello(active) }
            }
            "intrusion" -> mutableState.update {
                it.copy(notice = "Сервер сообщил: чужая попытка входа или подписи. Пара не разорвана")
            }
            "pair_deleted" -> {
                val current = store.pair.value ?: return
                endPair(
                    current,
                    PairEndReasons.describe(JsonFields.string(signal, "reason"), JsonFields.string(signal, "by"), current.idleSeconds),
                )
            }
        }
    }

    private suspend fun handleMessage(active: ActiveLink, data: String) {
        when (val opened = codec.open(data, active.pair.pair, Wire.FROM_PHONE, active.direction)) {
            is OpenResult.Opened -> {
                val envelope = opened.envelope
                if (processed.firstTime(envelope.id)) {
                    when (val parsed = MessageParser.parseFromPc(envelope.inner)) {
                        is ParseResult.Rejected -> mutableState.update { it.copy(dropped = it.dropped + 1) }
                        is ParseResult.Accepted -> handleParsed(active, parsed.message)
                    }
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

    private fun handleParsed(active: ActiveLink, message: PcMessage) {
        when (message) {
            is PcMessage.Hello -> onHello(active, message)
            is PcMessage.Snapshot -> onSnapshotPart(active, message)
            else -> model.apply(message)
        }
    }

    private fun onSnapshotPart(active: ActiveLink, part: PcMessage.Snapshot) {
        if (part.part == 1L) snapshotWaitToken++
        when (val step = assembler.accept(part)) {
            SnapshotStep.Incomplete -> scheduleSnapshotRequest(active, SNAPSHOT_WAIT_MS)
            SnapshotStep.Discarded -> {
                if (!requestSnapshot(active)) mutableState.update { it.copy(dropped = it.dropped + 1) }
            }
            is SnapshotStep.Complete -> {
                snapshotWaitToken++
                model.applySnapshot(step.parts)
            }
        }
    }

    private fun isSamePair(active: ActiveLink): Boolean = link?.pair?.pair == active.pair.pair

    private fun requestSnapshot(active: ActiveLink): Boolean {
        val now = clock.elapsedMs()
        if (helloPolicy.tryRequest(now)) {
            tracked { sendHello(active) }
            return true
        }
        if (helloPolicy.exhausted) return false
        scheduleSnapshotRequest(active, helloPolicy.waitMs(now))
        return true
    }

    private fun watchSnapshot(active: ActiveLink) = scheduleSnapshotRequest(active, SNAPSHOT_WAIT_MS)

    private fun scheduleSnapshotRequest(active: ActiveLink, delayMs: Long) {
        val token = ++snapshotWaitToken
        scope.launch {
            delay(delayMs)
            if (token == snapshotWaitToken && link === active) requestSnapshot(active)
        }
    }

    private fun extractId(data: String): String? =
        StrictJson.parseObjectOrNull(data)?.let { JsonFields.string(it, "id") }?.takeIf(JsonFields::isIdentifier)

    private fun onHello(active: ActiveLink, hello: PcMessage.Hello) {
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
        val token = snapshotWaitToken
        try {
            postQuietly { sendEnvelope(active, newId("ph"), HELLO_TTL_SECONDS, OutboundMessages.hello(model.feedPositions())) }
        } finally {
            if (token == snapshotWaitToken) watchSnapshot(active)
        }
    }

    private suspend fun ack(id: String) {
        postSigned("acks", "{\"ids\":[\"$id\"]}".encodeToByteArray()).requireSuccess()
    }

    private suspend fun sendEnvelope(active: ActiveLink, id: String, ttl: Int, inner: JsonObject) {
        postSealed(sealed(active, id, ttl, inner))
    }

    private suspend fun deliver(active: ActiveLink, id: String, ttl: Int, inner: JsonObject) {
        val body = sealed(active, id, ttl, inner)
        var attempt = 0
        while (true) {
            try {
                postSealed(body)
                return
            } catch (timeout: TimeoutCancellationException) {
                if (!isSamePair(active) || ++attempt >= DELIVERY_ATTEMPTS) throw timeout
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (failure: Exception) {
                if (!isSamePair(active) || !isTransient(failure) || ++attempt >= DELIVERY_ATTEMPTS) throw failure
                delay(DELIVERY_BACKOFF_MS)
            }
        }
    }

    private fun isTransient(failure: Exception): Boolean =
        if (failure is RelayException) failure.status == 429 || failure.status >= 500 else true

    private fun sealed(active: ActiveLink, id: String, ttl: Int, inner: JsonObject): ByteArray = codec.seal(
        key = active.direction.phoneToPc,
        pair = active.pair.pair,
        from = Wire.FROM_PHONE,
        id = id,
        ts = Clock.System.now().epochSeconds,
        ttl = ttl,
        inner = inner,
    ).encodeToByteArray()

    private suspend fun postSealed(body: ByteArray) {
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
        return withTimeout(POST_TIMEOUT_MS) {
            api.post(
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
    }

    private suspend fun postQuietly(block: suspend () -> Unit) {
        try {
            block()
        } catch (timeout: TimeoutCancellationException) {
            return
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
        clearSessionData()
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

    private fun clearSessionData() {
        model.reset()
        processed.clear()
        assembler.reset()
        snapshotWaitToken++
        publish()
    }

    private fun publish() {
        mutableState.update { it.copy(content = model.content()) }
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
        const val RESPONSE_TTL_SECONDS = 600
        const val HELLO_TTL_SECONDS = 120
        const val STALE_RETRIES = 3
        const val SNAPSHOT_WAIT_MS = 10_000L
        const val POST_TIMEOUT_MS = 15_000L
        const val NO_CONNECTION = "Нет связи с сервером, повторите"
        const val DELIVERY_ATTEMPTS = 5
        const val DELIVERY_BACKOFF_MS = 2_000L
        const val ID_DIGEST_BYTES = 12
        const val ID_RANDOM_BYTES = 12
        const val USER_DELETE_BODY = "{\"reason\":\"user\"}"
        const val NO_HELLO_DELETE_BODY = "{\"reason\":\"no_hello\"}"
    }
}
