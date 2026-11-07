package ru.aokazantsev.pult.relay

import ru.aokazantsev.pult.protocol.SseEvent

interface RelayApi {

    suspend fun get(url: String, headers: Map<String, String> = emptyMap()): RelayResponse

    suspend fun post(url: String, headers: Map<String, String> = emptyMap(), body: ByteArray = ByteArray(0)): RelayResponse

    suspend fun openStream(
        url: String,
        headers: Map<String, String>,
        onOpen: suspend () -> Unit,
        onEvent: suspend (SseEvent) -> Unit,
    )
}
