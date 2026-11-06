package ru.aokazantsev.pult.relay

data class ConnectionSettings(
    val serverUrl: String,
    val pairId: String,
    val token: String,
) {
    val baseUrl: String
        get() {
            val trimmed = serverUrl.trim().trimEnd('/')
            return if ("://" in trimmed) trimmed else "http://$trimmed"
        }

    val isComplete: Boolean
        get() = serverUrl.isNotBlank() && pairId.isNotBlank() && token.isNotBlank()
}
