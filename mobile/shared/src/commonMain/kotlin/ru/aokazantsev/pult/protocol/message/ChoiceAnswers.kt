package ru.aokazantsev.pult.protocol.message

object ChoiceAnswers {

    const val OTHER_LIMIT = 2000

    fun isValid(request: Request.Choice, answers: List<ChoiceAnswer>): Boolean {
        if (!request.answerable || answers.isEmpty()) return false
        val questions = request.questions.associateBy { it.id }
        val answered = HashSet<String>()
        for (answer in answers) {
            val question = questions[answer.question] ?: return false
            if (!answered.add(answer.question)) return false
            if (!isValidFor(question, answer)) return false
        }
        return answered == questions.keys
    }

    private fun isValidFor(question: ChoiceQuestion, answer: ChoiceAnswer): Boolean {
        val optionIds = question.options.map { it.id }.toSet()
        if (answer.selected.toSet().size != answer.selected.size || !optionIds.containsAll(answer.selected)) return false
        val other = answer.other
        if (other != null) {
            if (!question.other || ProtocolText.isBlank(other)) return false
            if (ProtocolText.codePointLength(other) > OTHER_LIMIT) return false
        }
        return if (question.multi) {
            answer.selected.isNotEmpty() || other != null
        } else {
            (answer.selected.size == 1 && other == null) || (answer.selected.isEmpty() && other != null)
        }
    }
}
