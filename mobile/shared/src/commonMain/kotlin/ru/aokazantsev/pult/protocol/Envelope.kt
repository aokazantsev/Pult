package ru.aokazantsev.pult.protocol

import kotlinx.serialization.json.JsonObject

data class Envelope(
    val id: String,
    val from: String,
    val ttl: Int,
    val inner: JsonObject,
)
