package ru.aokazantsev.pult.relay

class RelayResponse(val status: Int, val body: String) {
    val isSuccess: Boolean
        get() = status in 200..299

    fun requireSuccess(): RelayResponse {
        if (!isSuccess) throw RelayException(status, body)
        return this
    }
}
