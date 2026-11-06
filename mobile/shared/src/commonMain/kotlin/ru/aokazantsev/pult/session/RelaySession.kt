package ru.aokazantsev.pult.session

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.Inject
import dev.zacsweers.metro.SingleIn
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineDispatcher
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.joinAll
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.protocol.InboundMessage
import ru.aokazantsev.pult.protocol.InboundParser
import ru.aokazantsev.pult.protocol.OutboundMessages
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.relay.ConnectionSettings
import ru.aokazantsev.pult.relay.RelayApi
import ru.aokazantsev.pult.relay.SettingsStore

@OptIn(ExperimentalCoroutinesApi::class)
@SingleIn(AppScope::class)
@Inject
class RelaySession(
    private val api: RelayApi,
    private val settingsStore: SettingsStore,
    dispatcher: CoroutineDispatcher = Dispatchers.Default,
) {

    private val scope = CoroutineScope(SupervisorJob() + dispatcher.limitedParallelism(1))
    private val lifecycleLock = Mutex()
    private val queue = CardQueue()
    private val inFlight = LinkedHashSet<Job>()
    private var connectionJob: Job? = null
    private var activeSettings: ConnectionSettings? = null

    private val mutableState = MutableStateFlow(SessionState())
    val state: StateFlow<SessionState> = mutableState

    fun start() {
        scope.launch { lifecycleLock.withLock { startLocked() } }
    }

    fun stop() {
        scope.launch { lifecycleLock.withLock { stopLocked() } }
    }

    private fun startLocked() {
        if (connectionJob?.isActive == true) return
        connectionJob = scope.launch {
            settingsStore.settings.collectLatest { settings ->
                if (settings == null || !settings.isComplete) {
                    resetFor(null)
                    mutableState.update { it.copy(connection = ConnectionState.NotConfigured) }
                } else {
                    resetFor(settings)
                    connectLoop(settings)
                }
            }
        }
    }

    private suspend fun stopLocked() {
        val job = connectionJob ?: return
        withTimeoutOrNull(STOP_WAIT_MS) { joinAll(*inFlight.toTypedArray()) }
        job.cancel()
        job.join()
        connectionJob = null
        mutableState.update { it.copy(connection = ConnectionState.Idle) }
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
        val settings = activeSettings ?: return
        queue.markSending(key)
        publish()
        val id = responseId(key, inner)
        tracked {
            try {
                api.postMessage(settings, id = id, ttl = RESPONSE_TTL_SECONDS, inner = inner)
                queue.markSent(key)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (failure: Exception) {
                queue.markFailed(key, failure.message ?: "Не отправлено")
            }
            publish()
        }
    }

    private fun resetFor(settings: ConnectionSettings?) {
        if (settings != activeSettings) {
            queue.clear()
            activeSettings = settings
            publish()
        }
    }

    private suspend fun connectLoop(settings: ConnectionSettings) {
        while (true) {
            mutableState.update { it.copy(connection = ConnectionState.Connecting) }
            try {
                coroutineScope {
                    val pinger = launch { pingLoop(settings) }
                    api.openStream(
                        settings = settings,
                        onOpen = {
                            mutableState.update { it.copy(connection = ConnectionState.Connected) }
                            tracked { postQuietly { api.postMessage(settings, newId("hello"), 0, OutboundMessages.hello()) } }
                        },
                        onEvent = { event -> handle(settings, event) },
                    )
                    pinger.cancel()
                }
                mutableState.update { it.copy(connection = ConnectionState.Disconnected("Поток закрыт сервером")) }
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (failure: Exception) {
                mutableState.update {
                    it.copy(connection = ConnectionState.Disconnected(failure.message ?: failure::class.simpleName.orEmpty()))
                }
            }
            delay(RETRY_DELAY_MS)
        }
    }

    private suspend fun pingLoop(settings: ConnectionSettings) {
        while (true) {
            postQuietly { api.ping(settings) }
            delay(PING_INTERVAL_MS)
        }
    }

    private suspend fun handle(settings: ConnectionSettings, event: SseEvent) {
        if (event.event != "message") return
        val envelope = InboundParser.parseEnvelope(event.data) ?: return
        queue.accept(envelope)
        publish()
        tracked { postQuietly { api.postAcks(settings, listOf(envelope.id)) } }
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

    private fun responseId(key: String, inner: JsonObject): String =
        "r-${key.take(KEY_ID_LIMIT)}-${inner.toString().hashCode().toUInt().toString(16)}"

    private fun newId(prefix: String): String =
        "$prefix-${kotlin.random.Random.nextLong().toULong().toString(16)}"

    private companion object {
        const val PING_INTERVAL_MS = 15_000L
        const val RETRY_DELAY_MS = 2_000L
        const val STOP_WAIT_MS = 2_000L
        const val RESPONSE_TTL_SECONDS = 120
        const val KEY_ID_LIMIT = 40
    }
}
