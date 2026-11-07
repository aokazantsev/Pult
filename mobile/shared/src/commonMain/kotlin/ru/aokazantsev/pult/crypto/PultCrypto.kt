package ru.aokazantsev.pult.crypto

interface PultCrypto {
    fun randomBytes(size: Int): ByteArray

    fun sha256(data: ByteArray): ByteArray

    fun hmacSha256(key: ByteArray, data: ByteArray): ByteArray

    fun aesGcmEncrypt(key: ByteArray, nonce: ByteArray, aad: ByteArray, plaintext: ByteArray): ByteArray

    fun aesGcmDecrypt(key: ByteArray, nonce: ByteArray, aad: ByteArray, sealed: ByteArray): ByteArray?

    fun isValidPublicPoint(point: ByteArray): Boolean
}
