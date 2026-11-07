package ru.aokazantsev.pult.session

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.awaitCancellation
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.relay.RelayApi
import ru.aokazantsev.pult.relay.RelayResponse

class FakeRelayApi : RelayApi {

    class Call(val method: String, val url: String, val headers: Map<String, String>, val body: ByteArray)

    val calls = ArrayList<Call>()
    var failMessagePosts = 0
    var failStatus = 503
    var failBody = "{\"error\":\"busy\"}"
    var challengeResponse = RelayResponse(200, "{\"challenge\":\"ch1\"}")
    private var handler: (suspend (SseEvent) -> Unit)? = null
    var hangPosts = setOf<String>()
    private var closed = CompletableDeferred<Unit>()
    private var streams = 0

    override suspend fun get(url: String, headers: Map<String, String>): RelayResponse {
        calls.add(Call("GET", url, headers, ByteArray(0)))
        return RelayResponse(200, "{\"name\":\"pult-relay\",\"protocol\":[2]}")
    }

    override suspend fun post(url: String, headers: Map<String, String>, body: ByteArray): RelayResponse {
        calls.add(Call("POST", url, headers, body))
        if (hangPosts.any { url.endsWith(it) }) awaitCancellation()
        return when {
            url.endsWith("/challenge") -> challengeResponse
            url.endsWith("/messages") && failMessagePosts > 0 -> {
                failMessagePosts--
                RelayResponse(failStatus, failBody)
            }
            else -> RelayResponse(202, "{}")
        }
    }

    override suspend fun openStream(
        url: String,
        headers: Map<String, String>,
        onOpen: suspend () -> Unit,
        onEvent: suspend (SseEvent) -> Unit,
    ) {
        calls.add(Call("STREAM", url, headers, ByteArray(0)))
        handler = onEvent
        val current = CompletableDeferred<Unit>().also { closed = it }
        onOpen()
        onEvent(SseEvent(null, "server", "{\"type\":\"session\",\"stream\":\"s${++streams}\"}"))
        current.await()
    }

    fun closeStream() {
        closed.complete(Unit)
    }

    suspend fun emit(event: SseEvent) {
        handler?.invoke(event)
    }

    fun urls(suffix: String): List<Call> = calls.filter { it.url.endsWith(suffix) }
}
