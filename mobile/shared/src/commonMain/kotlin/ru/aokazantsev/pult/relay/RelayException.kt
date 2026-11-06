package ru.aokazantsev.pult.relay

class RelayException(val status: Int, val code: String?) : Exception("Сервер ответил $status${code?.let { ": $it" }.orEmpty()}")
