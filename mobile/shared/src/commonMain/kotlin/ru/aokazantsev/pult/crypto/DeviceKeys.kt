package ru.aokazantsev.pult.crypto

interface DeviceKeys {
    val signPublic: ByteArray
    val agreePublic: ByteArray

    fun sign(data: ByteArray): ByteArray

    fun agree(peerAgreePublic: ByteArray): ByteArray
}
