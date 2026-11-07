package ru.aokazantsev.pult.session

object PairEndReasons {

    fun describe(reason: String?, by: String?): String {
        val who = when (by) {
            "pc" -> "ПК"
            "phone" -> "телефон"
            "server" -> "сервер"
            else -> null
        }
        val what = when (reason) {
            "user" -> "отвязана вручную"
            "idle" -> "простой дольше заданного срока"
            "replaced" -> "заменена новым сопряжением"
            "pairing_rejected" -> "ПК отклонил сопряжение: проверка не прошла"
            "no_hello" -> "сопряжение не завершено"
            "pc_offline" -> "ПК не был на связи дольше 5 минут"
            else -> "причина неизвестна"
        }
        return if (who != null) "Пара разорвана ($who): $what" else "Пара разорвана: $what"
    }
}
