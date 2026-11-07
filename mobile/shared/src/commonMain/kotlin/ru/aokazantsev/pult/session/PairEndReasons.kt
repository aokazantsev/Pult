package ru.aokazantsev.pult.session

import ru.aokazantsev.pult.render.TextSanitizer

object PairEndReasons {

    const val PHONE_UNPAIRED = "Телефон отвязан"
    const val GONE = "Сессии больше нет: её разорвала другая сторона"
    private const val REASON_LIMIT = 64
    const val NO_HELLO = "Сопряжение не завершено, отсканируйте новый QR"

    fun describe(reason: String?, by: String?, idleSeconds: Int): String = when (reason) {
        "user" -> if (by == "phone") PHONE_UNPAIRED else "Сессию разорвали на ПК"
        "idle" -> "Сессия закрыта: нет событий ${IdleDuration.format(idleSeconds)}"
        "replaced" -> if (by == "phone") "Телефон сопряжён заново" else "На ПК начата новая сессия"
        "pairing_rejected" -> "ПК отклонил сопряжение: проверка не прошла"
        "no_hello" -> NO_HELLO
        "pc_offline" -> "ПК был без связи дольше 5 минут"
        null -> "Сессия разорвана"
        else -> "Сессия разорвана (${TextSanitizer.oneLine(reason).take(REASON_LIMIT)})"
    }
}
