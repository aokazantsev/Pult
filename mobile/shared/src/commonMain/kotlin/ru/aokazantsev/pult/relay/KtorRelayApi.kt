package ru.aokazantsev.pult.relay

import dev.zacsweers.metro.AppScope
import dev.zacsweers.metro.ContributesBinding
import dev.zacsweers.metro.Inject
import io.ktor.client.HttpClient
import io.ktor.client.request.HttpRequestBuilder
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.prepareGet
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsChannel
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.HttpHeaders
import io.ktor.http.content.ByteArrayContent
import io.ktor.utils.io.readLine
import ru.aokazantsev.pult.protocol.SseEvent
import ru.aokazantsev.pult.protocol.SseParser

@Inject
@ContributesBinding(AppScope::class)
class KtorRelayApi(private val client: HttpClient) : RelayApi {

    override suspend fun get(url: String, headers: Map<String, String>): RelayResponse =
        client.get(url) { applyHeaders(headers) }.toRelayResponse()

    override suspend fun post(url: String, headers: Map<String, String>, body: ByteArray): RelayResponse =
        client.post(url) {
            applyHeaders(headers)
            setBody(ByteArrayContent(body, ContentType.Application.Json))
        }.toRelayResponse()

    override suspend fun openStream(
        url: String,
        headers: Map<String, String>,
        onOpen: suspend () -> Unit,
        onEvent: suspend (SseEvent) -> Unit,
    ) {
        client.prepareGet(url) {
            applyHeaders(headers)
            header(HttpHeaders.Accept, "text/event-stream")
        }.execute { response ->
            if (response.status.value !in 200..299) {
                throw RelayException(response.status.value, response.bodyAsText())
            }
            onOpen()
            val channel = response.bodyAsChannel()
            val parser = SseParser()
            while (true) {
                val line = channel.readLine() ?: break
                parser.feed(line)?.let { onEvent(it) }
            }
        }
    }

    private fun HttpRequestBuilder.applyHeaders(headers: Map<String, String>) {
        headers.forEach { (name, value) -> header(name, value) }
    }

    private suspend fun HttpResponse.toRelayResponse() = RelayResponse(status.value, bodyAsText())
}
