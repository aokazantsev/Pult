package ru.aokazantsev.pult.pairing

import ru.aokazantsev.pult.protocol.Wire

object ProtocolCompatibility {

    fun pcVersionProblem(versions: List<Long>): String? = when {
        Wire.VERSION in versions -> null
        versions.maxOrNull()?.let { it > Wire.VERSION } == true -> "Приложение на телефоне старше ПК: обновите Pult на телефоне"
        else -> "Версия Pult на ПК несовместима: обновите Pult на ПК"
    }

    fun serverVersionProblem(versions: List<Long>): String? = when {
        Wire.VERSION in versions -> null
        versions.maxOrNull()?.let { it > Wire.VERSION } == true -> "Сервер новее приложения: обновите Pult на телефоне"
        else -> "Сервер несовместим: обновите сервер Pult"
    }
}
