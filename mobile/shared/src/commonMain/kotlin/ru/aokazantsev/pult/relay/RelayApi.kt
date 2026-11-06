package ru.aokazantsev.pult.relay

import kotlinx.serialization.json.JsonObject
import ru.aokazantsev.pult.protocol.SseEvent

interface RelayApi {

    suspend fun openStream(
        settings: ConnectionSettings,
        onOpen: suspend () -> Unit,
        onEvent: suspend (SseEvent) -> Unit,
    )

    suspend fun postMessage(settings: ConnectionSettings, id: String, ttl: Int, inner: JsonObject)

    suspend fun postAcks(settings: ConnectionSettings, ids: List<String>)

    suspend fun ping(settings: ConnectionSettings)
}
