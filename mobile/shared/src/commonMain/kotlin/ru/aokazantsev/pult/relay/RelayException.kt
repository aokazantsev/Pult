package ru.aokazantsev.pult.relay

import ru.aokazantsev.pult.protocol.StrictJson
import ru.aokazantsev.pult.protocol.JsonFields

class RelayException(val status: Int, val body: String) : Exception("Сервер ответил $status") {

    val code: String? = StrictJson.parseObjectOrNull(body)?.let { JsonFields.string(it, "error") }

    val reason: String? = StrictJson.parseObjectOrNull(body)?.let { JsonFields.string(it, "reason") }

    val by: String? = StrictJson.parseObjectOrNull(body)?.let { JsonFields.string(it, "by") }
}
