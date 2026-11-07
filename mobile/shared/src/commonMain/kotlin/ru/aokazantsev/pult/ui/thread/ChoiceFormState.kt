package ru.aokazantsev.pult.ui.thread

import ru.aokazantsev.pult.protocol.message.ChoiceAnswer
import ru.aokazantsev.pult.protocol.message.ChoiceAnswers
import ru.aokazantsev.pult.protocol.message.ChoiceQuestion
import ru.aokazantsev.pult.protocol.message.ProtocolText
import ru.aokazantsev.pult.protocol.message.Request

data class ChoiceFormState(
    val selected: Map<String, Set<String>> = emptyMap(),
    val other: Map<String, String> = emptyMap(),
) {

    fun toggle(question: ChoiceQuestion, optionId: String): ChoiceFormState {
        val current = selected[question.id].orEmpty()
        val next = when {
            question.multi && optionId in current -> current - optionId
            question.multi -> current + optionId
            optionId in current -> emptySet()
            else -> setOf(optionId)
        }
        val keepOther = question.multi || next.isEmpty()
        return copy(
            selected = selected + (question.id to next),
            other = if (keepOther) other else other - question.id,
        )
    }

    fun withOther(question: ChoiceQuestion, text: String): ChoiceFormState {
        val limited = ProtocolText.takeCodePoints(text, ChoiceAnswers.OTHER_LIMIT)
        val clearSelection = !question.multi && !ProtocolText.isBlank(limited)
        return copy(
            selected = if (clearSelection) selected - question.id else selected,
            other = other + (question.id to limited),
        )
    }

    fun answers(questions: List<ChoiceQuestion>): List<ChoiceAnswer> = questions.map { question ->
        val chosen = selected[question.id].orEmpty()
        val text = other[question.id]?.takeIf { question.other && !ProtocolText.isBlank(it) }
        ChoiceAnswer(
            question = question.id,
            selected = question.options.map { it.id }.filter { it in chosen },
            other = text,
        )
    }

    fun isComplete(request: Request.Choice): Boolean = ChoiceAnswers.isValid(request, answers(request.questions))
}
