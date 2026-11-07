package ru.aokazantsev.pult.pairing

class QrPayload(
    val versions: List<Long>,
    val server: String,
    val pair: String,
    val secret: ByteArray,
    val pcSign: String,
    val pcAgree: String,
    val idleSeconds: Int,
)
