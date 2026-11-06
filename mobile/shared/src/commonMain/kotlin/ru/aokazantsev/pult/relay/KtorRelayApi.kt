package ru.aokazantsev.pult.relay

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.ContributesBinding
import dev.zacsweers.metro.Inject
import io.ktor.client.HttpClient
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.prepareGet
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsChannel
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.content.TextContent
import io.ktor.utils.io.readLine
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlin.time.Clock
import ru.aokazantsev.pult.protocol.OutboundMessages
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.protocol.SseParser

@Inject
@ContributesBinding(AppScope::class)
class KtorRelayApi(private val client: HttpClient) : RelayApi {

    override suspend fun openStream(
        settings: ConnectionSettings,
        onOpen: suspend () -> Unit,
        onEvent: suspend (SseEvent) -> Unit,
    ) {
        client.prepareGet(url(settings, "stream")) {
            header(HttpHeaders.Authorization, "Bearer ${settings.token}")
            header(HttpHeaders.Accept, "text/event-stream")
        }.execute { response ->
            ensureSuccess(response)
            onOpen()
            val channel = response.bodyAsChannel()
            val parser = SseParser()
            while (true) {
                val line = channel.readLine() ?: break
                parser.feed(line)?.let { onEvent(it) }
            }
        }
    }

    override suspend fun postMessage(settings: ConnectionSettings, id: String, ttl: Int, inner: JsonObject) {
        val envelope = JsonObject(
            mapOf(
                "v" to JsonPrimitive(OutboundMessages.PROTOCOL_VERSION),
                "pair" to JsonPrimitive(settings.pairId),
                "from" to JsonPrimitive("phone"),
                "id" to JsonPrimitive(id),
                "ts" to JsonPrimitive(Clock.System.now().epochSeconds),
                "ttl" to JsonPrimitive(ttl),
                "inner" to inner,
            ),
        )
        postJson(settings, "messages", envelope)
    }

    override suspend fun postAcks(settings: ConnectionSettings, ids: List<String>) {
        postJson(settings, "acks", JsonObject(mapOf("ids" to JsonArray(ids.map(::JsonPrimitive)))))
    }

    override suspend fun ping(settings: ConnectionSettings) {
        postJson(settings, "ping", JsonObject(emptyMap()))
    }

    private suspend fun postJson(settings: ConnectionSettings, path: String, body: JsonObject) {
        val response = client.post(url(settings, path)) {
            header(HttpHeaders.Authorization, "Bearer ${settings.token}")
            setBody(TextContent(body.toString(), ContentType.Application.Json))
        }
        ensureSuccess(response)
    }

    private suspend fun ensureSuccess(response: HttpResponse) {
        if (response.status.value in 200..299) return
        val code = runCatching {
            (Json.parseToJsonElement(response.bodyAsText()) as? JsonObject)?.get("error")
                ?.let { (it as? JsonPrimitive)?.contentOrNull }
        }.getOrNull()
        throw RelayException(status = response.status.value, code = code)
    }

    private fun url(settings: ConnectionSettings, path: String): String =
        "${settings.baseUrl}/v1/pairs/${settings.pairId}/$path"
}
