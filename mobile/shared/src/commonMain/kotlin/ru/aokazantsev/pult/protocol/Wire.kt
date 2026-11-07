package ru.aokazantsev.pult.protocol

object Wire {

    const val VERSION = 2L

    const val FROM_PC = "pc"
    const val FROM_PHONE = "phone"

    fun loginString(pair: String, from: String, challenge: String): String =
        listOf("pult-v2-login", pair, from, challenge).joinToString("\n")

    fun postString(op: String, pair: String, from: String, stream: String, n: Long, bodySha256: String): String =
        listOf("pult-v2-post", op, pair, from, stream, n.toString(), bodySha256).joinToString("\n")

    fun transcript(pair: String, pcSign: String, pcAgree: String, phoneSign: String, phoneAgree: String): String =
        listOf(pair, pcSign, pcAgree, phoneSign, phoneAgree).joinToString("\n")

    fun aad(version: String, pair: String, from: String, id: String, ts: String, ttl: String): String =
        listOf("pult-v2-env", version, pair, from, id, ts, ttl).joinToString("\n")
}
