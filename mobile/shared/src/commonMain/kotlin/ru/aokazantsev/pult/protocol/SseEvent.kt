package ru.aokazantsev.pult.protocol

data class SseEvent(val id: String?, val event: String, val data: String)
