package ru.aokazantsev.pult.ui

import ru.aokazantsev.pult.protocol.Outcome
import ru.aokazantsev.pult.protocol.message.RequestKind
import ru.aokazantsev.pult.protocol.message.ThreadInfo
import ru.aokazantsev.pult.protocol.message.ThreadStatus
import ru.aokazantsev.pult.protocol.message.PultMode
import ru.aokazantsev.pult.protocol.message.TurnNote
import ru.aokazantsev.pult.session.CardState
import ru.aokazantsev.pult.session.RequestCard
import ru.aokazantsev.pult.session.ThreadMarks

object Labels {

    const val PC_DIALOG = "Стоит в диалоге на ПК"

    fun status(info: ThreadInfo): String = when (info.status) {
        ThreadStatus.Unknown -> "нет событий"
        ThreadStatus.Working -> "работает"
        ThreadStatus.Done -> "закончил"
        ThreadStatus.Error -> info.error?.let { "ошибка: $it" } ?: "ошибка"
        ThreadStatus.Ended -> "закрыт"
    }

    fun marks(marks: ThreadMarks): List<String> = buildList {
        if (marks.awaitsAction) add("ждёт действия") else if (marks.childAwaitsAction) add("ждёт действия (дочерний тред)")
        if (marks.awaitsText) add("ждёт текста") else if (marks.childAwaitsText) add("ждёт текста (дочерний тред)")
    }

    fun mode(mode: PultMode?): String? = when (mode) {
        null -> null
        PultMode.Phone -> "запросы идут на телефон"
        PultMode.Pc -> "запросы идут на ПК"
        PultMode.Unknown -> "режим неизвестен"
    }

    fun turnNote(note: TurnNote?): String? = when (note) {
        TurnNote.ContinuationCap -> "Окно текста не открыто: предел продолжений подряд, ответ — на ПК"
        null -> null
    }

    fun cardState(card: RequestCard): String? = when (card.state) {
        CardState.Open -> card.error?.let { "Не отправлено: $it" }
        CardState.Sending -> "Отправка…"
        CardState.Sent -> "Отправлено, ждём ПК"
        CardState.Closed -> outcome(card.outcome, card.kind)
        CardState.Missing -> "Неизвестен: на ПК запроса нет"
    }

    fun kind(kind: RequestKind): String = when (kind) {
        RequestKind.Permission -> "Разрешение"
        RequestKind.Choice -> "Вопрос"
        RequestKind.Text -> "Окно текста"
        RequestKind.Other -> "Запрос"
    }

    fun outcome(outcome: Outcome?, kind: RequestKind): String = when (outcome) {
        Outcome.Answered -> "Принято"
        Outcome.Expired -> if (kind == RequestKind.Text) "Окно закрыто" else "Истёк"
        Outcome.ResolvedOnPc -> "Решено на ПК"
        Outcome.Unknown, null -> "Неизвестно, могло выполниться"
    }
}
